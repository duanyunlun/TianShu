using System.Reflection;
using System.Text;
using System.Text.Json;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;
using TianShu.Contracts.Workflows;
using AgentRosterEntry = TianShu.Contracts.Projections.AgentRosterEntry;
using AgentRosterProjection = TianShu.Contracts.Projections.AgentRosterProjection;
using ArtifactCollectionProjection = TianShu.Contracts.Projections.ArtifactCollectionProjection;
using ArtifactProjection = TianShu.Contracts.Projections.ArtifactProjection;
using TaskBoardItem = TianShu.Contracts.Projections.TaskBoardItem;
using TaskBoardProjection = TianShu.Contracts.Projections.TaskBoardProjection;
using WorkflowBoardProjection = TianShu.Contracts.Projections.WorkflowBoardProjection;
using TianShu.Execution.Runtime.Models;
using TianShu.Execution.Runtime;
using TianShu.Execution.Runtime.Events;
using Task = System.Threading.Tasks.Task;

namespace TianShu.Cli.Tests;

[Collection("ConsoleCapture")]
public sealed class TianShuCliIntegrationTests
{
    private static readonly Assembly CliAssembly = ReflectionTestHelper.LoadRequiredAssembly("TianShu.Cli");

    [Fact]
    public void FakeAgentRuntime_ShouldOnlyExposeTypedStreamEmitSurface()
    {
        var agentStreamEmit = typeof(FakeAgentRuntime)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(static method =>
                string.Equals(method.Name, "EmitStreamEvent", StringComparison.Ordinal)
                && method.GetParameters() is [{ ParameterType: not null } parameters]
                && parameters.ParameterType == typeof(AgentStreamEvent));

        Assert.Null(agentStreamEmit);
    }

    private static void EmitProjectedStreamEvent(FakeAgentRuntime runtime, AgentStreamEvent streamEvent)
        => runtime.EmitStreamEvent(ControlPlaneConversationStreamEventCompatibility.ToControlPlaneConversationStreamEvent(streamEvent));

    [Fact]
    public void Prepare_WhenWorkingDirectoryMissing_ThrowsDirectoryNotFoundException()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var bootstrapperType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeBootstrapper");
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "review", "start", "--thread-id", "thread-review-1", "--target", "custom", "--instructions", "检查 bootstrapper。" });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", Path.Combine(AppContext.BaseDirectory, "missing-working-dir"));

        var exception = Assert.Throws<TargetInvocationException>(() => ReflectionTestHelper.InvokeStaticMethod(bootstrapperType, "Prepare", command));
        Assert.IsType<DirectoryNotFoundException>(exception.InnerException);
    }

    [Fact]
    public void ResolveAppHostProjectPath_WhenAppHostProjectExists_ReturnsAppHostProject()
    {
        var bootstrapperType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeBootstrapper");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: false,
            createAppHostProject: true);
        var nestedDirectory = Path.Combine(workingDirectory, "tests", "deep");
        Directory.CreateDirectory(nestedDirectory);

        var resolved = ReflectionTestHelper.InvokeStaticMethod(bootstrapperType, "ResolveAppHostProjectPath", null, nestedDirectory);
        Assert.NotNull(resolved);
        Assert.EndsWith(Path.Combine("src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj"), Assert.IsType<string>(resolved));
    }

    [Fact]
    public void Prepare_WhenBuiltAppHostExecutableExists_PrefersCompiledHost()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var bootstrapperType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeBootstrapper");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true,
            createAppHostProject: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var executablePath = CreateBuiltAppHostExecutable(workingDirectory);

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "read", "--thread-id", "thread-read-compiled-host" });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", workingDirectory);
        ReflectionTestHelper.SetProperty(command, "ConfigFilePath", configPath);

        var bootstrap = ReflectionTestHelper.InvokeStaticMethod(bootstrapperType, "Prepare", command);
        Assert.NotNull(bootstrap);

        var runtimeOptions = ReflectionTestHelper.GetProperty(bootstrap!, "RuntimeOptions");
        Assert.NotNull(runtimeOptions);
        Assert.Equal(false, ReflectionTestHelper.GetProperty(runtimeOptions!, "UseDotNetProjectLauncher"));
        Assert.Equal(executablePath, ReflectionTestHelper.GetProperty(runtimeOptions, "ExecutablePath"));
    }

    [Fact]
    public void RunModelRouteDiagnostic_OutputJson_WritesStructuredDiagnosticWithoutSecrets()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDirectory = new TestTempDirectory();
        var workingDirectory = tempDirectory.Path;
        var configDirectory = Path.Combine(workingDirectory, ".tianshu");
        Directory.CreateDirectory(configDirectory);
        var configPath = Path.Combine(configDirectory, "tianshu.toml");
        File.WriteAllText(
            configPath,
            """
            profile = "work"
            model_route_set = "beta"
            provider = "openai"
            model = "gpt-5"

            [profiles.work]
            model_route_set = "beta"

            [providers.openai]
            base_url = "https://api.openai.example/v1"
            api_key_env = "OPENAI_SECRET_SHOULD_NOT_APPEAR"
            default_protocol = "responses"

            [providers.anthropic]
            base_url = "https://api.anthropic.example/v1"
            api_key_env = "ANTHROPIC_SECRET_SHOULD_NOT_APPEAR"
            default_protocol = "anthropic_messages"

            [model_route_sets.beta]
            display_name = "Beta route set"
            routes = [{ kind = "coding", candidates = [{ provider = "openai", model = "gpt-5-codex", protocol = "responses", capabilities = ["code"] }, { provider = "anthropic", model = "claude-3-7-sonnet" }] }]
            """,
            new UTF8Encoding(false));

        var command = ParseCommandWithWorkspace(
            parserType,
            ["model-route", "route", "--route", "coding", "--route-set", "beta", "--json"],
            workingDirectory,
            configPath);
        var runner = CreateRunnerWithRuntimeFactory(runnerType, new FakeAgentRuntime());

        var (exitCode, output) = InvokeRunnerAndCaptureOutput(runner, "RunModelRouteDiagnostic", command);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("SECRET_SHOULD_NOT_APPEAR", output, StringComparison.Ordinal);
        Assert.DoesNotContain("api.openai.example", output, StringComparison.Ordinal);
        Assert.NotEqual("{}", output.Trim());

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal("host.model_route.diagnostic", root.GetProperty("projectionKind").GetString());
        Assert.Equal("beta", root.GetProperty("routeSetId").GetString());
        Assert.Equal("coding", root.GetProperty("requestedRouteKind").GetString());
        Assert.Equal("coding", root.GetProperty("resolvedRouteKind").GetString());
        Assert.False(root.GetProperty("routeSetIsVirtual").GetBoolean());

        var preferred = root.GetProperty("preferredCandidate");
        Assert.Equal("openai", preferred.GetProperty("provider").GetString());
        Assert.Equal("gpt-5-codex", preferred.GetProperty("model").GetString());
        Assert.Equal("responses", preferred.GetProperty("protocol").GetString());
        Assert.Equal(0, preferred.GetProperty("candidateIndex").GetInt32());
        Assert.Equal("code", Assert.Single(preferred.GetProperty("capabilities").EnumerateArray()).GetString());

        var fallback = Assert.Single(root.GetProperty("fallbackCandidates").EnumerateArray());
        Assert.Equal("anthropic", fallback.GetProperty("provider").GetString());
        Assert.Equal("claude-3-7-sonnet", fallback.GetProperty("model").GetString());
        Assert.Equal(1, fallback.GetProperty("candidateIndex").GetInt32());
        Assert.Equal(2, root.GetProperty("candidates").GetArrayLength());
    }

    [Fact]
    public void ResolveAppHostProjectPath_WhenConfiguredRelativePathProvided_UsesWorkingDirectoryBase()
    {
        var bootstrapperType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeBootstrapper");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: false,
            createAppHostProject: true);

        var resolved = ReflectionTestHelper.InvokeStaticMethod(
            bootstrapperType,
            "ResolveAppHostProjectPath",
            Path.Combine(".", "src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj"),
            workingDirectory);

        Assert.NotNull(resolved);
        Assert.Equal(
            Path.Combine(workingDirectory, "src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj"),
            Assert.IsType<string>(resolved));
    }

    [Fact]
    public void Prepare_WhenOnlyPublishedAppHostExecutableExists_UsesExecutableLaunch()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var bootstrapperType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeBootstrapper");
        var workingDirectory = CreateIsolatedWorkspace(createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var executablePath = CreatePublishedAppHostExecutable(workingDirectory);

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "read", "--thread-id", "thread-read-published-host" });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", workingDirectory);
        ReflectionTestHelper.SetProperty(command, "ConfigFilePath", configPath);

        var bootstrap = ReflectionTestHelper.InvokeStaticMethod(bootstrapperType, "Prepare", command);
        Assert.NotNull(bootstrap);

        var runtimeOptions = ReflectionTestHelper.GetProperty(bootstrap!, "RuntimeOptions");
        Assert.NotNull(runtimeOptions);
        Assert.Equal(false, ReflectionTestHelper.GetProperty(runtimeOptions!, "UseDotNetProjectLauncher"));
        Assert.Equal(executablePath, ReflectionTestHelper.GetProperty(runtimeOptions, "ExecutablePath"));
        Assert.Null(ReflectionTestHelper.GetProperty(runtimeOptions, "AppHostProjectPath"));
        Assert.Null(ReflectionTestHelper.GetProperty(bootstrap, "AppHostProjectPath"));
    }

    [Fact]
    public void ResolveForTesting_WhenUserAppHostDirectoryContainsExecutable_UsesUserLevelProbe()
    {
        var resolverType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliAppHostLaunchResolver");
        using var tempDir = new TestTempDirectory();
        var workingDirectory = Path.Combine(tempDir.Path, "workspace");
        var appBaseDirectory = Path.Combine(tempDir.Path, "app");
        var userProfileDirectory = Path.Combine(tempDir.Path, "profile");
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(appBaseDirectory);
        var userAppHostDirectory = Path.Combine(userProfileDirectory, ".tianshu", "runtime", "apphost");
        Directory.CreateDirectory(userAppHostDirectory);
        var executablePath = Path.Combine(userAppHostDirectory, "TianShu.AppHost.exe");
        File.WriteAllText(executablePath, "stub", new UTF8Encoding(false));

        var resolution = ReflectionTestHelper.InvokeStaticMethod(
            resolverType,
            "ResolveForTesting",
            null,
            workingDirectory,
            appBaseDirectory,
            userProfileDirectory);
        Assert.NotNull(resolution);
        Assert.Equal(executablePath, ReflectionTestHelper.GetProperty(resolution!, "AppHostExecutablePath"));
        Assert.Null(ReflectionTestHelper.GetProperty(resolution, "AppHostProjectPath"));
    }

    [Fact]
    public void ResolveForTesting_WhenTianShuHomeContainsExecutable_UsesTianShuHomeProbe()
    {
        var resolverType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliAppHostLaunchResolver");
        using var tempDir = new TestTempDirectory();
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var workingDirectory = Path.Combine(tempDir.Path, "workspace");
        var appBaseDirectory = Path.Combine(tempDir.Path, "app");
        var tianShuHome = Path.Combine(tempDir.Path, "tianshu-home");
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(appBaseDirectory);
        var appHostDirectory = Path.Combine(tianShuHome, "runtime", "apphost");
        Directory.CreateDirectory(appHostDirectory);
        var executablePath = Path.Combine(appHostDirectory, "TianShu.AppHost.exe");
        File.WriteAllText(executablePath, "stub", new UTF8Encoding(false));

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

            var resolution = ReflectionTestHelper.InvokeStaticMethod(
                resolverType,
                "ResolveForTesting",
                null,
                workingDirectory,
                appBaseDirectory,
                null);

            Assert.NotNull(resolution);
            Assert.Equal(executablePath, ReflectionTestHelper.GetProperty(resolution!, "AppHostExecutablePath"));
            Assert.Null(ReflectionTestHelper.GetProperty(resolution, "AppHostProjectPath"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
        }
    }

    [Fact]
    public void Prepare_AppliesResolvedConfigToRuntimeOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var bootstrapperType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeBootstrapper");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true,
            createAppHostProject: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "thread", "read", "--thread-id", "thread-read-1" });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", workingDirectory);
        ReflectionTestHelper.SetProperty(command, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(command, "ProfileName", "work");

        var bootstrap = ReflectionTestHelper.InvokeStaticMethod(bootstrapperType, "Prepare", command);
        Assert.NotNull(bootstrap);

        var runtimeOptions = ReflectionTestHelper.GetProperty(bootstrap!, "RuntimeOptions");
        Assert.NotNull(runtimeOptions);
        Assert.Equal(workingDirectory, ReflectionTestHelper.GetProperty(runtimeOptions!, "WorkingDirectory"));
        Assert.Equal(configPath, ReflectionTestHelper.GetProperty(runtimeOptions, "ConfigFilePath"));
        Assert.Equal("claude-3-7-sonnet", ReflectionTestHelper.GetProperty(runtimeOptions, "Model"));
        Assert.Equal("anthropic", ReflectionTestHelper.GetProperty(runtimeOptions, "ModelProvider"));
        Assert.Equal("on-request", ReflectionTestHelper.GetProperty(runtimeOptions, "ApprovalPolicy")?.ToString());
        Assert.Equal("anthropic_messages", ReflectionTestHelper.GetProperty(runtimeOptions, "ProviderWireApi"));
        Assert.Equal("openai-responses", ReflectionTestHelper.GetProperty(runtimeOptions, "ProtocolAdapter"));

        var appHostProjectPath = ReflectionTestHelper.GetProperty(bootstrap, "AppHostProjectPath");
        Assert.NotNull(appHostProjectPath);
        Assert.EndsWith(Path.Combine("src", "Hosting", "TianShu.AppHost", "TianShu.AppHost.csproj"), Assert.IsType<string>(appHostProjectPath));
    }

    [Fact]
    public void Prepare_WhenDynamicToolsProvided_AppliesThemToRuntimeOptions()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var bootstrapperType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeBootstrapper");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "thread",
                "start",
                "--dynamic-tools-json",
                """[{"name":"mcp__calendar__find_events","description":"搜索日历事件。","inputSchema":{"type":"object"}}]""",
            });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", workingDirectory);
        ReflectionTestHelper.SetProperty(command, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(command, "ProfileName", "work");

        var bootstrap = ReflectionTestHelper.InvokeStaticMethod(bootstrapperType, "Prepare", command);
        Assert.NotNull(bootstrap);

        var runtimeOptions = ReflectionTestHelper.GetProperty(bootstrap!, "RuntimeOptions");
        Assert.NotNull(runtimeOptions);
        var dynamicTools = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ReflectionTestHelper.GetProperty(runtimeOptions!, "DynamicTools"));
        var tool = Assert.Single(dynamicTools.Cast<object>());
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("mcp__calendar__find_events", document.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_WithInjectedRuntime_PrintsReviewSummary()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "review", "start", "--thread-id", "thread-review-runtime", "--target", "custom", "--instructions", "检查 runner 集成输出。" });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", workingDirectory);
        ReflectionTestHelper.SetProperty(command, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(command, "ProfileName", "work");

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.StartReviewHandler = _ => new ControlPlaneReviewStartResult
        {
            ReviewThreadId = "review_runtime_001",
            Turn = new ControlPlaneReviewTurn
            {
                Id = "turn_runtime_001",
                Status = "inProgress",
                DisplayText = "检查 runner 集成输出。",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunRuntimeSurfaceAsync", command, CancellationToken.None);
            var exitCode = await ReflectionTestHelper.AwaitTaskResultAsync(task);
            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.NotNull(fakeRuntime.InitializedOptions);
        Assert.Equal("claude-3-7-sonnet", fakeRuntime.InitializedOptions!.Model);
        Assert.Equal("openai-responses", fakeRuntime.InitializedOptions.ProtocolAdapter);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var reviewRequest = Assert.Single(fakeRuntime.ReviewStartRequests);
        Assert.Equal("thread-review-runtime", reviewRequest.ThreadId);
        Assert.IsType<ControlPlaneReviewCustomTarget>(reviewRequest.Target);

        var output = writer.ToString();
        Assert.Contains("已启动 review。", output, StringComparison.Ordinal);
        Assert.Contains("reviewThreadId：review_runtime_001", output, StringComparison.Ordinal);
        Assert.Contains("turnId：turn_runtime_001", output, StringComparison.Ordinal);
        Assert.Contains("请求：检查 runner 集成输出。", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunDebugAsync_ClearMemories_WithoutStateDb_DoesNotStartRuntime_And_CleansMemoryRoot()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var workingDirectory = Path.Combine(tempDir.Path, "workspace");
        var kernelHome = Path.Combine(tempDir.Path, "kernel-home");
        var TianShuHome = Path.Combine(tempDir.Path, "tianshu-home");
        var memoriesRoot = Path.Combine(TianShuHome, "data", "memory");
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(memoriesRoot);
        File.WriteAllText(Path.Combine(memoriesRoot, "memory.txt"), "memo");

        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", TianShuHome);

            var parseResult = ReflectionTestHelper.InvokeStaticMethod(
                parserType,
                "Parse",
                (object)new[] { "debug", "clear-memories", "--json", "--cwd", workingDirectory });
            var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
            Assert.NotNull(command);

            var fakeRuntime = new FakeAgentRuntime();
            var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
            var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunDebugAsync", command!);

            Assert.Equal(0, exitCode);
            Assert.Null(fakeRuntime.InitializedOptions);
            Assert.Equal(0, fakeRuntime.DiagnosticRpcCallCount);
            Assert.Empty(fakeRuntime.Invocations);
            Assert.False(Directory.Exists(memoriesRoot));

            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("usedRuntime").GetBoolean());
            Assert.False(document.RootElement.GetProperty("stateDbExists").GetBoolean());
            Assert.True(document.RootElement.GetProperty("memoryRootRemoved").GetBoolean());
            Assert.Equal(Path.Combine(kernelHome, "state.db"), document.RootElement.GetProperty("stateDbPath").GetString());
            Assert.Equal(memoriesRoot, document.RootElement.GetProperty("memoryRoot").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
        }
    }

    [Fact]
    public async Task RunDebugAsync_ClearMemories_WithStateDb_UsesDiagnosticsControlPlane()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var kernelHome = Path.Combine(tempDir.Path, "kernel-home");
        var TianShuHome = Path.Combine(tempDir.Path, "tianshu-home");
        Directory.CreateDirectory(kernelHome);
        Directory.CreateDirectory(TianShuHome);
        File.WriteAllText(Path.Combine(kernelHome, "state.db"), "seed");

        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", TianShuHome);

            var command = ParseCommandWithWorkspace(
                parserType,
                ["debug", "clear-memories"],
                workingDirectory,
                configPath);

            var fakeRuntime = new FakeAgentRuntime
            {
                ClearDebugMemoriesHandler = () => new ControlPlaneDebugClearMemoriesResult
                {
                    StateDbPath = Path.Combine(kernelHome, "state.db"),
                    MemoryRootPath = Path.Combine(TianShuHome, "data", "memory"),
                    RemovedMemoryRoot = true,
                },
            };

            var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
            var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunDebugAsync", command);

            Assert.Equal(0, exitCode);
            Assert.NotNull(fakeRuntime.InitializedOptions);
            Assert.False(fakeRuntime.InitializedOptions!.CreateThreadOnInitialize);
            Assert.Equal(0, fakeRuntime.DiagnosticRpcCallCount);
            Assert.Empty(fakeRuntime.Invocations);
            Assert.Equal(1, fakeRuntime.ClearDebugMemoriesCallCount);
            Assert.Contains("Cleared memory state from", output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
        }
    }

    [Fact]
    public async Task RunDebugAsync_ClearMemories_WithStateDb_JsonOutput_NormalizesKernelFields()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        using var tempDir = new TestTempDirectory();
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var kernelHome = Path.Combine(tempDir.Path, "kernel-home");
        var TianShuHome = Path.Combine(tempDir.Path, "tianshu-home");
        var memoryRoot = Path.Combine(TianShuHome, "data", "memory");
        Directory.CreateDirectory(kernelHome);
        Directory.CreateDirectory(TianShuHome);
        File.WriteAllText(Path.Combine(kernelHome, "state.db"), "seed");

        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", TianShuHome);

            var command = ParseCommandWithWorkspace(
                parserType,
                ["debug", "clear-memories", "--json"],
                workingDirectory,
                configPath);

            var fakeRuntime = new FakeAgentRuntime
            {
                ClearDebugMemoriesHandler = () => new ControlPlaneDebugClearMemoriesResult
                {
                    StateDbPath = Path.Combine(kernelHome, "state.db"),
                    ClearedStage1OutputCount = 2,
                    MemoryRootPath = memoryRoot,
                    RemovedMemoryRoot = true,
                },
            };

            var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
            var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunDebugAsync", command);

            Assert.Equal(0, exitCode);
            Assert.NotNull(fakeRuntime.InitializedOptions);
            Assert.False(fakeRuntime.InitializedOptions!.CreateThreadOnInitialize);
            Assert.Equal(0, fakeRuntime.DiagnosticRpcCallCount);
            Assert.Equal(1, fakeRuntime.ClearDebugMemoriesCallCount);

            using var document = JsonDocument.Parse(output);
            Assert.True(document.RootElement.GetProperty("usedRuntime").GetBoolean());
            Assert.True(document.RootElement.GetProperty("stateDbExists").GetBoolean());
            Assert.Equal(Path.Combine(kernelHome, "state.db"), document.RootElement.GetProperty("stateDbPath").GetString());
            Assert.Equal(memoryRoot, document.RootElement.GetProperty("memoryRoot").GetString());
            Assert.True(document.RootElement.GetProperty("memoryRootRemoved").GetBoolean());
            Assert.Equal(memoryRoot, document.RootElement.GetProperty("memoryRootPath").GetString());
            Assert.True(document.RootElement.GetProperty("removedMemoryRoot").GetBoolean());
            Assert.Contains("Cleared memory state from", document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
        }
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ExperimentalFeatureList_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["experimental-feature", "list", "--limit", "3", "--cursor", "cursor_01"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListExperimentalFeaturesHandler = _ => new ControlPlaneExperimentalFeatureCatalogResult
            {
                NextCursor = "cursor_02",
                Items =
                [
                    new ControlPlaneExperimentalFeatureDescriptor
                    {
                        Name = "tool_search",
                        Stage = "beta",
                        DisplayName = "Tool Search",
                        Enabled = true,
                        DefaultEnabled = false,
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var request = Assert.Single(fakeRuntime.ExperimentalFeatureListRequests);
        Assert.Equal(3, request.Limit);
        Assert.Equal("cursor_01", request.Cursor);

        Assert.Contains("tool_search\tbeta\tenabled=True\tdefault=False\tTool Search", output, StringComparison.Ordinal);
        Assert.Contains("nextCursor\tcursor_02", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_FeaturesList_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["features", "list"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListExperimentalFeaturesHandler = _ => new ControlPlaneExperimentalFeatureCatalogResult
            {
                Items =
                [
                    new ControlPlaneExperimentalFeatureDescriptor
                    {
                        Name = "zeta",
                        Stage = "underDevelopment",
                        Enabled = true,
                    },
                    new ControlPlaneExperimentalFeatureDescriptor
                    {
                        Name = "alpha",
                        Stage = "beta",
                        Enabled = false,
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var request = Assert.Single(fakeRuntime.ExperimentalFeatureListRequests);
        Assert.Null(request.Limit);
        Assert.Null(request.Cursor);

        var lines = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("alpha", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("zeta", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_FeaturesEnable_WithInjectedRuntime_ValidatesFeatureAndWritesConfig()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["features", "enable", "unified_exec"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListExperimentalFeaturesHandler = _ => new ControlPlaneExperimentalFeatureCatalogResult
            {
                Items =
                [
                    new ControlPlaneExperimentalFeatureDescriptor
                    {
                        Name = "unified_exec",
                        Stage = "stable",
                        Enabled = false,
                    },
                ],
            },
            WriteConfigValueHandler = request => new ControlPlaneConfigWriteResult
            {
                Status = "ok",
                Version = "v1",
                FilePath = configPath,
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Single(fakeRuntime.ExperimentalFeatureListRequests);
        var request = Assert.Single(fakeRuntime.ConfigValueWriteRequests);
        Assert.Equal("features.unified_exec", request.KeyPath);
        Assert.NotNull(request.Value);
        Assert.Equal(StructuredValueKind.Boolean, request.Value!.Kind);
        Assert.True(request.Value.BooleanValue);
        Assert.Equal("replace", request.MergeStrategy);
        Assert.Equal(workingDirectory, request.WorkingDirectory);
        Assert.Contains("Enabled feature `unified_exec` in tianshu.toml.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_FeaturesEnable_WithUnknownFeature_DoesNotWriteConfig()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["features", "enable", "missing_feature"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListExperimentalFeaturesHandler = _ => new ControlPlaneExperimentalFeatureCatalogResult
            {
                Items =
                [
                    new ControlPlaneExperimentalFeatureDescriptor
                    {
                        Name = "unified_exec",
                        Stage = "stable",
                        Enabled = false,
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Single(fakeRuntime.ExperimentalFeatureListRequests);
        Assert.Empty(fakeRuntime.ConfigValueWriteRequests);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_CollaborationModeList_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["collaboration-mode", "list"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListCollaborationModesHandler = () => new ControlPlaneCollaborationModeCatalogResult
            {
                Items =
                [
                    new ControlPlaneCollaborationModeDescriptor
                    {
                        Name = "plan",
                        Mode = "plan",
                        Model = "gpt-5.4",
                        ReasoningEffort = "high",
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Contains("plan\tplan\tgpt-5.4\thigh", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunMcpAsync_McpList_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["mcp", "list", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadConfigHandler = _ => new ControlPlaneConfigSnapshotResult
            {
                Config = StructuredValueTestHelper.FromJson(
                    """
                    {
                      "mcp_servers": {
                        "demo": {
                          "command": "node",
                          "args": ["server.js"],
                          "env": {
                            "FOO": "bar"
                          }
                        }
                      }
                    }
                    """),
            },
            ListMcpServerStatusHandler = _ => new ControlPlaneMcpServerCatalogResult
            {
                Items =
                [
                    new ControlPlaneMcpServerDescriptor
                    {
                        Name = "demo",
                        AuthStatus = "authorized",
                        ToolNames = ["search", "read"],
                        ResourceUris = ["resource://demo/index"],
                        ResourceTemplateUris = ["resource://demo/{id}"],
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunMcpAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var readRequest = Assert.Single(fakeRuntime.ConfigReadRequests);
        Assert.Null(readRequest.WorkingDirectory);
        var request = Assert.Single(fakeRuntime.McpServerStatusListRequests);
        Assert.Equal(200, request.Limit);
        Assert.Null(request.Cursor);

        using var document = JsonDocument.Parse(output);
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("demo", item.GetProperty("name").GetString());
        Assert.Equal("authorized", item.GetProperty("auth_status").GetString());
        Assert.Equal("node", item.GetProperty("transport").GetProperty("command").GetString());
    }

    [Fact]
    public async Task RunMcpAsync_McpList_TextMasksEnvAndEnvVars_AndRendersDisabledReason()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["mcp", "list"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadConfigHandler = _ => new ControlPlaneConfigSnapshotResult
            {
                Config = StructuredValueTestHelper.FromJson(
                    """
                    {
                      "mcp_servers": {
                        "docs": {
                          "command": "docs-server",
                          "args": ["--port", "4000"],
                          "env": {
                            "TOKEN": "super-secret-list"
                          },
                          "env_vars": ["APP_TOKEN", "WORKSPACE_ID"],
                          "enabled": false,
                          "disabled_reason": "managed by policy"
                        }
                      }
                    }
                    """),
            },
            ListMcpServerStatusHandler = _ => new ControlPlaneMcpServerCatalogResult
            {
                Items =
                [
                    new ControlPlaneMcpServerDescriptor
                    {
                        Name = "docs",
                        AuthStatus = "authorized",
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunMcpAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Contains("Status", output, StringComparison.Ordinal);
        Assert.Contains("disabled: managed by policy", output, StringComparison.Ordinal);
        Assert.Contains("TOKEN=*****", output, StringComparison.Ordinal);
        Assert.Contains("APP_TOKEN=*****", output, StringComparison.Ordinal);
        Assert.Contains("WORKSPACE_ID=*****", output, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-list", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunMcpAsync_McpGet_TextMasksEnvAndEnvVars()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["mcp", "get", "docs"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadConfigHandler = _ => new ControlPlaneConfigSnapshotResult
            {
                Config = StructuredValueTestHelper.FromJson(
                    """
                    {
                      "mcp_servers": {
                        "docs": {
                          "command": "docs-server",
                          "args": ["--port", "4000"],
                          "env": {
                            "TOKEN": "super-secret-get"
                          },
                          "env_vars": ["APP_TOKEN"],
                          "enabled": true
                        }
                      }
                    }
                    """),
            },
            ListMcpServerStatusHandler = _ => new ControlPlaneMcpServerCatalogResult
            {
                Items =
                [
                    new ControlPlaneMcpServerDescriptor
                    {
                        Name = "docs",
                        AuthStatus = "authorized",
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunMcpAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Contains("docs", output, StringComparison.Ordinal);
        Assert.Contains("transport: stdio", output, StringComparison.Ordinal);
        Assert.Contains("env: TOKEN=*****, APP_TOKEN=*****", output, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-get", output, StringComparison.Ordinal);
        Assert.DoesNotContain("auth_status:", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunMcpAsync_McpGet_WhenDisabled_PrintsSingleLineWithReason()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["mcp", "get", "docs"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadConfigHandler = _ => new ControlPlaneConfigSnapshotResult
            {
                Config = StructuredValueTestHelper.FromJson(
                    """
                    {
                      "mcp_servers": {
                        "docs": {
                          "command": "docs-server",
                          "enabled": false,
                          "disabled_reason": "managed by policy"
                        }
                      }
                    }
                    """),
            },
            ListMcpServerStatusHandler = _ => new ControlPlaneMcpServerCatalogResult
            {
                Items =
                [
                    new ControlPlaneMcpServerDescriptor
                    {
                        Name = "docs",
                        AuthStatus = "authorized",
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunMcpAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal("docs (disabled: managed by policy)", output.TrimEnd());
    }

    [Fact]
    public async Task RunMcpAsync_McpGetJson_OmitsAuthStatus_ButKeepsFields()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["mcp", "get", "docs", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadConfigHandler = _ => new ControlPlaneConfigSnapshotResult
            {
                Config = StructuredValueTestHelper.FromJson(
                    """
                    {
                      "mcp_servers": {
                        "docs": {
                          "command": "docs-server",
                          "args": ["--port", "4000"],
                          "env": {
                            "TOKEN": "super-secret-get-json"
                          },
                          "env_vars": ["APP_TOKEN"],
                          "enabled": true,
                          "enabled_tools": ["search"],
                          "disabled_tools": ["read"],
                          "startup_timeout_sec": 12.5,
                          "tool_timeout_sec": 30
                        }
                      }
                    }
                    """),
            },
            ListMcpServerStatusHandler = _ => new ControlPlaneMcpServerCatalogResult
            {
                Items =
                [
                    new ControlPlaneMcpServerDescriptor
                    {
                        Name = "docs",
                        AuthStatus = "authorized",
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunMcpAsync", command);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal("docs", root.GetProperty("name").GetString());
        Assert.True(root.GetProperty("enabled").GetBoolean());
        Assert.True(root.TryGetProperty("disabled_reason", out var disabledReason));
        Assert.Equal(JsonValueKind.Null, disabledReason.ValueKind);
        Assert.Equal("docs-server", root.GetProperty("transport").GetProperty("command").GetString());
        Assert.Equal("APP_TOKEN", root.GetProperty("transport").GetProperty("env_vars")[0].GetString());
        Assert.Equal("search", root.GetProperty("enabled_tools")[0].GetString());
        Assert.Equal("read", root.GetProperty("disabled_tools")[0].GetString());
        Assert.False(root.TryGetProperty("auth_status", out _));
    }

    [Fact]
    public async Task RunMcpAsync_McpAdd_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["mcp", "add", "demo", "--env", "FOO=bar", "--", "node", "server.js"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            WriteConfigBatchHandler = _ => new ControlPlaneConfigWriteResult(),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunMcpAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var request = Assert.Single(fakeRuntime.ConfigBatchWriteRequests);
        Assert.Null(request.WorkingDirectory);
        Assert.Equal(configPath, request.FilePath);
        Assert.Contains(request.Items, static item => item.KeyPath == "mcp_servers.demo.command" && item.Value?.StringValue == "node");
        Assert.Contains(request.Items, static item => item.KeyPath == "mcp_servers.demo.args" && item.Value is { Kind: StructuredValueKind.Array, Items.Count: 1 } && item.Value.Items[0].StringValue == "server.js");
        Assert.Contains(request.Items, static item => item.KeyPath == "mcp_servers.demo.env" && item.Value is { Kind: StructuredValueKind.Object } && item.Value.Properties["FOO"].StringValue == "bar");
        Assert.Contains("Added global MCP server 'demo'.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ConversationSummary_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["conversation-summary", "--thread-id", "thread-summary-runtime"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetConversationSummaryHandler = _ => new ControlPlaneConversationArtifact
            {
                ConversationId = "thread-summary-runtime",
                Source = "rollout",
                Path = "Test/cli-acceptance-artifacts/runtime-summary.json",
                WorkingDirectory = workingDirectory,
                UpdatedAt = "2026-03-21T14:21:00Z",
                Preview = "会话摘要已生成。",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var request = Assert.Single(fakeRuntime.ConversationSummaryRequests);
        Assert.Equal("thread-summary-runtime", request.ThreadId?.Value);
        Assert.Null(request.RolloutPath);

        Assert.Contains("会话：thread-summary-runtime", output, StringComparison.Ordinal);
        Assert.Contains("来源：rollout", output, StringComparison.Ordinal);
        Assert.Contains("路径：Test/cli-acceptance-artifacts/runtime-summary.json", output, StringComparison.Ordinal);
        Assert.Contains("摘要：会话摘要已生成。", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_GitDiffToRemote_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["git-diff", "--thread-id", "thread_diff_runtime"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetGitDiffToRemoteHandler = _ => new ControlPlaneGitDiffArtifact
            {
                HasChanges = true,
                Diff = "diff --git a/foo.txt b/foo.txt\n+new line\n",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.GitDiffToRemoteRequests);
        Assert.Equal("thread_diff_runtime", request.ThreadId.Value);
        Assert.Contains("diff --git a/foo.txt b/foo.txt", output, StringComparison.Ordinal);
        Assert.Contains("+new line", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_PluginRead_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[]
            {
                "plugin",
                "read",
                "--marketplace-path",
                "D:\\marketplace",
                "--plugin-name",
                "demo-plugin",
            });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", workingDirectory);
        ReflectionTestHelper.SetProperty(command, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(command, "ProfileName", "work");

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadPluginHandler = _ => new ControlPlanePluginReadResult
            {
                Plugin = new ControlPlanePluginDetail
                {
                    MarketplaceName = "debug",
                    MarketplacePath = "D:/marketplace/marketplace.json",
                    Summary = new ControlPlanePluginSummary
                    {
                        Id = "demo-plugin@debug",
                        Name = "demo-plugin",
                        Source = StructuredValue.FromPlainObject(new Dictionary<string, object?>
                        {
                            ["type"] = "local",
                            ["path"] = "D:/marketplace/demo-plugin",
                        }),
                        Installed = true,
                        Enabled = true,
                        InstallPolicy = "AVAILABLE",
                        AuthPolicy = "ON_INSTALL",
                    },
                    Description = "demo description",
                    Skills =
                    [
                        new ControlPlanePluginSkillReference
                        {
                            Name = "demo-plugin:search",
                            Description = "search skill",
                            Path = "D:/marketplace/demo-plugin/skills/search",
                        },
                    ],
                    Apps =
                    [
                        new ControlPlanePluginAppReference
                        {
                            Id = "connector_example",
                            Name = "connector_example",
                            InstallUrl = "https://chatgpt.com/apps/connector_example/connector_example",
                        },
                    ],
                    McpServers = ["demo"],
                },
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunRuntimeSurfaceAsync", command, CancellationToken.None);
            var exitCode = await ReflectionTestHelper.AwaitTaskResultAsync(task);
            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.NotNull(fakeRuntime.InitializedOptions);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var request = Assert.Single(fakeRuntime.PluginReadRequests);
        Assert.Equal(Path.GetFullPath("D:\\marketplace"), request.MarketplacePath);
        Assert.Equal("demo-plugin", request.PluginName);

        var output = writer.ToString();
        Assert.Contains("已读取插件。", output, StringComparison.Ordinal);
        Assert.Contains("插件：demo-plugin", output, StringComparison.Ordinal);
        Assert.Contains("键：demo-plugin@debug", output, StringComparison.Ordinal);
        Assert.Contains("来源：local", output, StringComparison.Ordinal);
        Assert.Contains("技能数：1", output, StringComparison.Ordinal);
        Assert.Contains("应用数：1", output, StringComparison.Ordinal);
        Assert.Contains("MCP Server 数：1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_PluginInstall_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["plugin", "install", "--marketplace-path", "D:\\marketplace", "--plugin-name", "demo-plugin"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            InstallPluginHandler = _ => new ControlPlanePluginInstallResult
            {
                AuthPolicy = "ON_INSTALL",
                AppsNeedingAuth =
                [
                    new ControlPlanePluginAppReference
                    {
                        Id = "connector_example",
                        Name = "connector_example",
                        InstallUrl = "https://chatgpt.com/apps/connector_example/connector_example",
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.PluginInstallRequests);
        Assert.Equal(Path.GetFullPath("D:\\marketplace"), request.MarketplacePath);
        Assert.Equal("demo-plugin", request.PluginName);
        Assert.Equal(workingDirectory, request.WorkingDirectory);

        Assert.Contains("插件安装请求已完成。", output, StringComparison.Ordinal);
        Assert.Contains("以下应用仍需授权：", output, StringComparison.Ordinal);
        Assert.Contains("connector_example\tconnector_example\thttps://chatgpt.com/apps/connector_example/connector_example", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_Read_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "read", "--thread-id", "thread-read-runtime", "--include-turns"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadThreadHandler = _ => new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-read-runtime"),
                    WorkingDirectory = "D:/Work/TianShu",
                    Preview = "runner 线程读取验证",
                    Turns =
                    [
                        new ControlPlaneThreadTurn { Id = "turn_1", Status = "completed" },
                        new ControlPlaneThreadTurn { Id = "turn_2", Status = "completed" },
                    ],
                },
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.NotNull(fakeRuntime.InitializedOptions);
        Assert.Equal("claude-3-7-sonnet", fakeRuntime.InitializedOptions!.Model);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.ThreadReadRequests);
        Assert.Equal("thread-read-runtime", request.ThreadId.Value);
        Assert.True(request.IncludeTurns);

        Assert.Contains("已读取线程。", output, StringComparison.Ordinal);
        Assert.Contains("线程：thread-read-runtime", output, StringComparison.Ordinal);
        Assert.Contains("工作目录：D:/Work/TianShu", output, StringComparison.Ordinal);
        Assert.Contains("标题：runner 线程读取验证", output, StringComparison.Ordinal);
        Assert.Contains("轮次：2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_List_WithInjectedRuntime_UsesTypedRequestAndWritesEnvelope()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            [
                "thread",
                "list",
                "--limit",
                "2",
                "--cursor",
                "cursor_thread_001",
                "--sort-key",
                "updated_at",
                "--model-provider",
                "anthropic",
                "--model-provider",
                "openai",
                "--source-kind",
                "subAgentReview",
                "--source-kind",
                "appServer",
                "--search-term",
                "配置",
                "--all-cwd",
                "--json",
            ],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListThreadsHandler = request => new AgentThreadListResult
            {
                Data =
                [
                    new AgentThreadInfo
                    {
                        ThreadId = "thread-list-runtime-001",
                        Preview = "配置列表验证",
                        Name = "配置 GUI 线程",
                        Cwd = "D:/Work/TianShu",
                        UpdatedAt = new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero),
                    },
                ],
                NextCursor = "cursor_thread_002",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var request = Assert.Single(fakeRuntime.ThreadListRequests);
        Assert.Equal(2, request.Limit);
        Assert.Equal("cursor_thread_001", request.Cursor);
        Assert.Equal("updated_at", request.SortKey);
        Assert.Equal(["anthropic", "openai"], request.ModelProviders);
        Assert.Equal(
            new[]
            {
                ControlPlaneThreadSourceKind.SubAgentReview,
                ControlPlaneThreadSourceKind.AppServer,
            },
            request.SourceKinds);
        Assert.Equal("配置", request.SearchTerm);
        Assert.Null(request.WorkingDirectory);

        using var document = JsonDocument.Parse(output);
        Assert.Equal("cursor_thread_002", document.RootElement.GetProperty("nextCursor").GetString());
        var thread = Assert.Single(document.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal("thread-list-runtime-001", thread.GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task RunThreadAsync_Start_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            [
                "thread",
                "start",
                "--thread-model",
                "gpt-5",
                "--thread-model-provider",
                "anthropic",
                "--thread-service-tier",
                "fast",
                "--thread-approval-policy",
                "on-request",
                "--thread-sandbox-mode",
                "workspace-write",
                "--thread-personality",
                "friendly",
                "--thread-ephemeral",
                "true",
                "--thread-persist-extended-history",
                "true",
                "--thread-experimental-raw-events",
                "true",
                "--thread-dynamic-tools-json",
                "[{\"name\":\"task_lookup\",\"description\":\"查询任务\",\"inputSchema\":{\"type\":\"object\"}}]",
                "--json",
            ],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            StartNewThreadRequestHandler = request => new AgentThreadInfo
            {
                ThreadId = "thread-start-runtime-001",
                Preview = request.ModelProvider ?? "start 线程验证",
                Name = "start 线程验证",
                Cwd = "D:/Work/TianShu",
                UpdatedAt = new DateTimeOffset(2026, 3, 12, 7, 30, 0, TimeSpan.Zero),
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Empty(fakeRuntime.StartThreadCalls);
        var request = Assert.Single(fakeRuntime.StartThreadRequests);
        Assert.Equal("gpt-5", request.Model);
        Assert.Equal("anthropic", request.ModelProvider);
        Assert.Equal("fast", request.ServiceTier);
        Assert.Equal("on-request", request.ApprovalPolicy);
        Assert.Equal("workspace-write", request.SandboxMode);
        Assert.Equal("friendly", request.Personality);
        Assert.True(request.Ephemeral);
        Assert.True(request.PersistExtendedHistory);
        Assert.True(request.ExperimentalRawEvents);
        var dynamicTool = Assert.Single(request.DynamicTools!);
        Assert.Equal("task_lookup", dynamicTool.Name);
        Assert.Equal("查询任务", dynamicTool.Description);
        Assert.Equal("object", dynamicTool.InputSchema.Properties["type"].StringValue);

        using var document = JsonDocument.Parse(output[(output.IndexOf('{'))..]);
        Assert.Equal("thread-start-runtime-001", document.RootElement.GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task RunThreadAsync_Fork_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "fork", "--thread-id", "thread-fork-runtime", "--model-provider", "anthropic"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ForkThreadHandler = _ => new AgentThreadInfo
            {
                ThreadId = "thread-fork-runtime-child",
                Preview = "fork 线程验证",
                Name = "fork 线程验证",
                Cwd = "D:/Work/TianShu",
                UpdatedAt = new DateTimeOffset(2026, 3, 12, 8, 0, 0, TimeSpan.Zero),
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Collection(fakeRuntime.ForkThreadRequests, threadId => Assert.Equal("thread-fork-runtime", threadId));
        Assert.Empty(fakeRuntime.Invocations);
        Assert.Contains("已分叉线程。", output, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(output[(output.IndexOf('{'))..]);
        Assert.Equal("thread-fork-runtime-child", document.RootElement.GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task RunThreadAsync_Delete_WithMatchingConfirmation_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "delete", "--thread-id", "thread-delete-runtime", "--confirm", "thread-delete-runtime"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            DeleteThreadHandler = static _ => true,
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Collection(fakeRuntime.DeleteThreadRequests, threadId => Assert.Equal("thread-delete-runtime", threadId));
        Assert.Contains("已删除线程。", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_Delete_WithWrongConfirmation_DoesNotCallRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "delete", "--thread-id", "thread-delete-runtime", "--confirm", "wrong", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            DeleteThreadHandler = static _ => throw new InvalidOperationException("delete 不应被调用。"),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Empty(fakeRuntime.DeleteThreadRequests);

        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.True(document.RootElement.GetProperty("cancelled").GetBoolean());
    }

    [Fact]
    public async Task RunThreadAsync_Clear_WithMatchingConfirmation_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "clear", "--confirm", "DELETE ALL THREADS"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ClearThreadsHandler = static () => new ControlPlaneClearThreadsResult { DeletedCount = 3 },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Equal(1, fakeRuntime.ClearThreadsCallCount);
        Assert.Contains("已清空线程：3 个。", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_Clear_WithWrongConfirmation_DoesNotCallRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "clear", "--confirm", "delete all threads", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ClearThreadsHandler = static () => throw new InvalidOperationException("clear 不应被调用。"),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(1, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Equal(0, fakeRuntime.ClearThreadsCallCount);

        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.True(document.RootElement.GetProperty("cancelled").GetBoolean());
    }

    [Fact]
    public async Task RunThreadAsync_Resume_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            [
                "thread",
                "resume",
                "--thread-id",
                "thread-resume-runtime",
                "--thread-model-provider",
                "anthropic",
                "--thread-approval-policy",
                "on-request",
                "--thread-personality",
                "friendly",
                "--thread-persist-extended-history",
                "true",
                "--thread-history-json",
                "[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"恢复上下文\"}]}]",
            ],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ResumeThreadRequestHandler = request => new AgentThreadResumeResult
            {
                ThreadId = request.ThreadId.Value,
                Preview = "resume 线程验证",
                Name = "resume 线程验证",
                Cwd = "D:/Work/TianShu",
                SeedHistory = [new AgentThreadSeedHistoryItem { Role = "user", Content = "resume" }],
                Turns = [new AgentThreadTurn { Id = "turn-resume-runtime-001", Status = "completed" }],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Empty(fakeRuntime.ResumeThreadCalls);

        var request = Assert.Single(fakeRuntime.ResumeThreadRequests);
        Assert.Equal("thread-resume-runtime", request.ThreadId.Value);
        Assert.Equal("anthropic", request.ModelProvider);
        Assert.Equal("on-request", request.ApprovalPolicy);
        Assert.Equal("friendly", request.Personality);
        Assert.True(request.PersistExtendedHistory);
        var history = Assert.Single(request.History!);
        Assert.Equal("message", history.Properties["type"].StringValue);
        Assert.Equal("assistant", history.Properties["role"].StringValue);
        var content = Assert.Single(history.Properties["content"].Items);
        Assert.Equal("output_text", content.Properties["type"].StringValue);
        Assert.Equal("恢复上下文", content.Properties["text"].StringValue);

        Assert.Contains("已恢复线程。", output, StringComparison.Ordinal);
        Assert.Contains("标题：resume 线程验证", output, StringComparison.Ordinal);
        Assert.Contains("种子历史：1", output, StringComparison.Ordinal);
        Assert.Contains("回合数：1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_Unarchive_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "unarchive", "--thread-id", "thread-unarchive-runtime"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            UnarchiveThreadHandler = _ => new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-unarchive-runtime"),
                    WorkingDirectory = "D:/Work/TianShu",
                    Preview = "runner 线程取消归档验证",
                },
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Collection(fakeRuntime.UnarchiveThreadRequests, threadId => Assert.Equal("thread-unarchive-runtime", threadId));
        Assert.Contains("已取消线程归档。", output, StringComparison.Ordinal);
        Assert.Contains("线程：thread-unarchive-runtime", output, StringComparison.Ordinal);
        Assert.Contains("标题：runner 线程取消归档验证", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_Metadata_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "metadata", "--thread-id", "thread-metadata-runtime", "--git-sha", "def456", "--clear-git-branch", "--git-origin-url", "https://example.com/repo.git"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            UpdateThreadMetadataHandler = _ => new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-metadata-runtime"),
                    WorkingDirectory = "D:/Work/TianShu",
                    Preview = "runner 线程元数据验证",
                    GitSha = "def456",
                    GitOriginUrl = "https://example.com/repo.git",
                },
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.ThreadMetadataUpdateRequests);
        Assert.Equal("thread-metadata-runtime", request.ThreadId.Value);
        Assert.True(request.HasGitSha);
        Assert.Equal("def456", request.GitSha);
        Assert.True(request.HasGitBranch);
        Assert.Null(request.GitBranch);
        Assert.True(request.HasGitOriginUrl);
        Assert.Equal("https://example.com/repo.git", request.GitOriginUrl);

        Assert.Contains("已更新线程元数据。", output, StringComparison.Ordinal);
        Assert.Contains("线程：thread-metadata-runtime", output, StringComparison.Ordinal);
        Assert.Contains("标题：runner 线程元数据验证", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_Rollback_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "rollback", "--thread-id", "thread-rollback-runtime", "--num-turns", "2"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            RollbackThreadHandler = _ => new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-rollback-runtime"),
                    WorkingDirectory = "D:/Work/TianShu",
                    Preview = "runner 线程回滚验证",
                    Turns =
                    [
                        new ControlPlaneThreadTurn { Id = "turn_rollback_1", Status = "completed" },
                    ],
                },
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.ThreadRollbackRequests);
        Assert.Equal("thread-rollback-runtime", request.ThreadId.Value);
        Assert.Equal(2, request.NumTurns);

        Assert.Contains("已回滚线程。", output, StringComparison.Ordinal);
        Assert.Contains("线程：thread-rollback-runtime", output, StringComparison.Ordinal);
        Assert.Contains("轮次：1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_Read_WithJsonOutput_UsesTypedRuntime_And_PrintsStructuredEnvelope()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "read", "--thread-id", "thread-read-json-runtime", "--include-turns", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadThreadHandler = _ => new ControlPlaneThreadOperationResult
            {
                Thread = new ControlPlaneThreadDetail
                {
                    ThreadId = new ThreadId("thread-read-json-runtime"),
                    Preview = "json 线程读取验证",
                    Turns =
                    [
                        new ControlPlaneThreadTurn { Id = "turn_json_1", Status = "completed" },
                    ],
                },
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        using var document = JsonDocument.Parse(output);
        var thread = document.RootElement.GetProperty("thread");
        Assert.Equal("thread-read-json-runtime", thread.GetProperty("id").GetString());
        Assert.Equal("json 线程读取验证", thread.GetProperty("preview").GetString());
        Assert.Single(thread.GetProperty("turns").EnumerateArray());
    }

    [Fact]
    public async Task RunThreadAsync_LoadedList_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "loaded-list", "--limit", "4", "--cursor", "thread_010"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListLoadedThreadsHandler = _ => new ControlPlaneLoadedThreadListResult
            {
                ThreadIds = [new ThreadId("thread_a"), new ThreadId("thread_b")],
                NextCursor = "thread_b",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.ThreadLoadedListRequests);
        Assert.Equal(4, request.Limit);
        Assert.Equal("thread_010", request.Cursor);

        Assert.Contains("thread_a", output, StringComparison.Ordinal);
        Assert.Contains("thread_b", output, StringComparison.Ordinal);
        Assert.Contains("nextCursor=thread_b", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_Compact_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "compact", "--thread-id", "thread-compact-runtime", "--keep-recent-turns", "6"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            CompactThreadHandler = static (_, _) => new ControlPlaneThreadCommandAcceptedResult(),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Collection(
            fakeRuntime.CompactThreadRequests,
            item =>
            {
                Assert.Equal("thread-compact-runtime", item.ThreadId);
                Assert.Equal(6, item.KeepRecentTurns);
            });
        Assert.Contains("已启动线程压缩。", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_CleanBackgroundTerminals_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "clean-background-terminals", "--thread-id", "thread-clean-runtime"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            CleanBackgroundTerminalsHandler = static _ => new ControlPlaneThreadCommandAcceptedResult(),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Collection(fakeRuntime.CleanBackgroundTerminalsRequests, threadId => Assert.Equal("thread-clean-runtime", threadId));
        Assert.Contains("已请求清理线程后台终端。", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_Unsubscribe_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "unsubscribe", "--thread-id", "thread-unsubscribe-runtime"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            UnsubscribeThreadHandler = _ => new ControlPlaneThreadUnsubscribeResult
            {
                Status = "notSubscribed",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Collection(fakeRuntime.UnsubscribeThreadRequests, threadId => Assert.Equal("thread-unsubscribe-runtime", threadId));
        Assert.Contains("当前连接未订阅该线程。", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_IncrementElicitation_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "increment-elicitation", "--thread-id", "thread-elicitation-runtime"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            IncrementThreadElicitationHandler = _ => new ControlPlaneThreadElicitationResult
            {
                Count = 1,
                Paused = true,
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Collection(fakeRuntime.IncrementThreadElicitationRequests, threadId => Assert.Equal("thread-elicitation-runtime", threadId));
        Assert.Contains("已递增线程挂起交互计数。", output, StringComparison.Ordinal);
        Assert.Contains("计数：1", output, StringComparison.Ordinal);
        Assert.Contains("paused：true", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunThreadAsync_DecrementElicitation_WithJsonOutput_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "decrement-elicitation", "--thread-id", "thread-elicitation-runtime", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            DecrementThreadElicitationHandler = _ => new ControlPlaneThreadElicitationResult
            {
                Count = 0,
                Paused = false,
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Collection(fakeRuntime.DecrementThreadElicitationRequests, threadId => Assert.Equal("thread-elicitation-runtime", threadId));

        using var document = JsonDocument.Parse(output);
        Assert.Equal(0UL, document.RootElement.GetProperty("count").GetUInt64());
        Assert.False(document.RootElement.GetProperty("paused").GetBoolean());
    }

    [Fact]
    public async Task RunThreadAsync_Compact_WithJsonOutput_UsesTypedRuntime_And_PrintsEmptyObject()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["thread", "compact", "--thread-id", "thread-compact-json-runtime", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            CompactThreadHandler = static (_, _) => new ControlPlaneThreadCommandAcceptedResult(),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunThreadAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        using var document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task RunFeedbackUploadAsync_WithTypedRuntime_UsesDiagnosticsControlPlane()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "feedback", "upload", "--classification", "bug", "--include-logs", "--thread-id", "thread_feedback_runtime" });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", workingDirectory);
        ReflectionTestHelper.SetProperty(command, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(command, "ProfileName", "work");

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.UploadFeedbackHandler = static _ => new ControlPlaneFeedbackUploadResult
        {
            ThreadId = "feedback_runtime_001",
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunFeedbackAsync", command, CancellationToken.None);
            var exitCode = await ReflectionTestHelper.AwaitTaskResultAsync(task);
            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Empty(fakeRuntime.GovernanceFeedbackUploadRequests);
        var request = Assert.Single(fakeRuntime.DiagnosticsFeedbackUploadRequests);
        Assert.Equal("bug", request.Classification);
        Assert.True(request.IncludeLogs);
        Assert.Equal("thread_feedback_runtime", request.ThreadId);
        var output = writer.ToString();
        Assert.Contains("trackingThreadId=feedback_runtime_001", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWindowsSandboxAsync_WithInjectedRuntime_WaitsForCompletionNotification()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "windows-sandbox", "setup-start", "--mode", "elevated", "--cwd", workingDirectory });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", workingDirectory);
        ReflectionTestHelper.SetProperty(command, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(command, "ProfileName", "work");

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.StartWindowsSandboxSetupHandler = request =>
        {
            EmitWindowsSandboxNotification(fakeRuntime);
            return new ControlPlaneWindowsSandboxSetupStartResult
            {
                Started = true,
            };
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);

        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var task = ReflectionTestHelper.InvokeMethod(runner, "RunWindowsSandboxAsync", command, CancellationToken.None);
            var exitCode = await ReflectionTestHelper.AwaitTaskResultAsync(task);
            Assert.Equal(0, Assert.IsType<int>(exitCode));
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var request = Assert.Single(fakeRuntime.WindowsSandboxSetupRequests);
        Assert.Equal(WindowsSandboxSetupMode.Elevated, request.Mode);
        Assert.Equal(workingDirectory, request.WorkingDirectory);
        Assert.Empty(fakeRuntime.Invocations);
        var output = writer.ToString();
        Assert.Contains("Windows Sandbox setup completed. mode=elevated", output, StringComparison.Ordinal);
        Assert.Contains("success=True", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRealtimeAsync_Start_WithInjectedRuntime_UsesTypedRuntime_And_PrintsResolvedSessionId()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var parseResult = ReflectionTestHelper.InvokeStaticMethod(
            parserType,
            "Parse",
            (object)new[] { "realtime", "start", "--thread-id", "thread_rt_runtime", "--prompt", "streaming prompt", "--json" });
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", workingDirectory);
        ReflectionTestHelper.SetProperty(command, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(command, "ProfileName", "work");

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.StartRealtimeHandler = request =>
        {
            EmitRealtimeStartedNotification(fakeRuntime);
            return new ControlPlaneRealtimeCommandAcceptedResult();
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRealtimeAsync", command);

        Assert.Equal(0, exitCode);
        var request = Assert.Single(fakeRuntime.RealtimeStartRequests);
        Assert.Equal("thread_rt_runtime", request.ThreadId.Value);
        Assert.Equal("streaming prompt", request.Prompt);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        using var document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.GetProperty("notificationReceived").GetBoolean());
        Assert.Equal("thread_rt_runtime", document.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("realtime_runtime_001", document.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("rpcResult").ValueKind);
    }

    [Fact]
    public async Task RunRealtimeAsync_HandoffOutput_WithInjectedRuntime_UsesTypedRealtimeMethod()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["realtime", "handoff-output", "--thread-id", "thread_rt_runtime", "--session-id", "session_rt_runtime", "--handoff-id", "call_rt_runtime", "--output", "delegated result"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.HandoffRealtimeOutputHandler = static _ => new ControlPlaneRealtimeCommandAcceptedResult();

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRealtimeAsync", command);

        Assert.Equal(0, exitCode);
        var request = Assert.Single(fakeRuntime.RealtimeHandoffOutputRequests);
        Assert.Equal("thread_rt_runtime", request.ThreadId.Value);
        Assert.Equal("session_rt_runtime", request.SessionId);
        Assert.Equal("call_rt_runtime", request.HandoffId);
        Assert.Equal("delegated result", request.Output);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Contains("Submitted realtime handoff output.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ExecPrompt_WithInjectedRuntime_UsesHeadlessSessionFlow()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ExecCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["exec", "--json", "-o", ".\\last-message.md", "--full-auto", "当前目录是什么？"],
            workingDirectory,
            configPath);
        var lastMessagePath = Assert.IsType<string>(ReflectionTestHelper.GetProperty(command, "OutputLastMessageFilePath"));

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.StartNewThreadAsyncHandler = _ =>
        {
            fakeRuntime.ActiveThreadId = "thread_exec_new_001";
            return Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
            {
                ThreadId = "thread_exec_new_001",
                Preview = "new exec thread",
                Cwd = workingDirectory,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        };
        fakeRuntime.SendAsyncHandler = (userMessage, _, _) =>
        {
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextDelta,
                fakeRuntime.ActiveThreadId,
                "turn_exec_new_001",
                text: "workspace is ready"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextCompleted,
                fakeRuntime.ActiveThreadId,
                "turn_exec_new_001"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.TurnCompleted,
                fakeRuntime.ActiveThreadId,
                "turn_exec_new_001",
                status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(
                "workspace is ready",
                turnId: "turn_exec_new_001",
                turnStatus: "completed"));
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Empty(fakeRuntime.Invocations);
        Assert.Single(fakeRuntime.StartThreadCalls);
        var sendCall = Assert.Single(fakeRuntime.SendCalls);
        Assert.Equal("当前目录是什么？", sendCall.UserMessage);
        Assert.NotNull(fakeRuntime.InitializedOptions);
        Assert.Equal("workspace-write", fakeRuntime.InitializedOptions!.SandboxMode);
        Assert.Equal("never", fakeRuntime.InitializedOptions.ApprovalPolicy);
        Assert.True(File.Exists(lastMessagePath));
        Assert.Equal("workspace is ready", File.ReadAllText(lastMessagePath));
        Assert.Collection(fakeRuntime.UnsubscribeThreadRequests, threadId => Assert.Equal("thread_exec_new_001", threadId));

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 2);
        Assert.Contains("\"type\":\"exec_completed\"", lines[^1], StringComparison.Ordinal);
        using var completedDocument = JsonDocument.Parse(lines[^1]);
        var tokenUsage = completedDocument.RootElement.GetProperty("tokenUsage");
        Assert.True(tokenUsage.GetProperty("estimated").GetBoolean());
        Assert.Equal("text_length_estimate", tokenUsage.GetProperty("source").GetString());
        Assert.True(tokenUsage.GetProperty("total").GetProperty("totalTokens").GetInt32() > 0);
    }

    [Fact]
    public async Task RunAsync_ExecPrompt_WithOutputSchema_InitializesRuntimeOptionsWithSchema()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ExecCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var schemaPath = Path.Combine(workingDirectory, "schema.json");
        File.WriteAllText(schemaPath, """{"type":"object","properties":{"answer":{"type":"string"}}}""");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["exec", "--output-schema", schemaPath, "只返回结构化结果"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.StartNewThreadAsyncHandler = _ =>
        {
            fakeRuntime.ActiveThreadId = "thread_exec_schema_001";
            return Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
            {
                ThreadId = "thread_exec_schema_001",
                Preview = "schema exec thread",
                Cwd = workingDirectory,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        };
        fakeRuntime.SendAsyncHandler = (userMessage, _, _) =>
        {
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.TurnCompleted,
                fakeRuntime.ActiveThreadId,
                "turn_exec_schema_001",
                status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(
                userMessage,
                turnId: "turn_exec_schema_001",
                turnStatus: "completed"));
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, _) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunAsync", command);

        Assert.Equal(0, exitCode);
        Assert.NotNull(fakeRuntime.InitializedOptions?.OutputSchema);
        var outputSchema = JsonSerializer.SerializeToElement(fakeRuntime.InitializedOptions!.OutputSchema!.ToPlainObject());
        Assert.Equal("object", outputSchema.GetProperty("type").GetString());
        Assert.Equal("string", outputSchema.GetProperty("properties").GetProperty("answer").GetProperty("type").GetString());
    }

    [Fact]
    public async Task RunAsync_ExecResumeLast_WithAll_FallsBackToNewThread_WhenNoThreadMatches()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ExecCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["exec", "resume", "--last", "--all", "继续处理剩余问题"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.ListThreadsHandler = _ => new AgentThreadListResult { Data = [] };
        fakeRuntime.StartNewThreadAsyncHandler = _ =>
        {
            fakeRuntime.ActiveThreadId = "thread_exec_resume_fallback_001";
            return Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
            {
                ThreadId = "thread_exec_resume_fallback_001",
                Preview = "fallback exec thread",
                Cwd = workingDirectory,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        };
        fakeRuntime.SendAsyncHandler = (userMessage, _, _) =>
        {
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextDelta,
                fakeRuntime.ActiveThreadId,
                "turn_exec_resume_fallback_001",
                text: "fallback ok"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextCompleted,
                fakeRuntime.ActiveThreadId,
                "turn_exec_resume_fallback_001"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.TurnCompleted,
                fakeRuntime.ActiveThreadId,
                "turn_exec_resume_fallback_001",
                status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(
                userMessage,
                turnId: "turn_exec_resume_fallback_001",
                turnStatus: "completed"));
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Contains("fallback ok", output, StringComparison.Ordinal);
        var listRequest = Assert.Single(fakeRuntime.ThreadListRequests);
        Assert.Equal(1, listRequest.Limit);
        Assert.Null(listRequest.WorkingDirectory);
        Assert.Equal("updated_at", listRequest.SortKey);
        Assert.Equal(["anthropic"], listRequest.ModelProviders);
        Assert.Single(fakeRuntime.StartThreadCalls);
        Assert.Empty(fakeRuntime.ResumeThreadCalls);
        var sendCall = Assert.Single(fakeRuntime.SendCalls);
        Assert.Equal("继续处理剩余问题", sendCall.UserMessage);
    }

    [Fact]
    public async Task RunAsync_ExecResumeExplicitTarget_ResumesMatchedThread_ByName()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ExecCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["exec", "resume", "thread_target_name", "继续跟进"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.ListThreadsHandler = _ => new AgentThreadListResult
        {
            Data =
            [
                new AgentThreadInfo
                {
                    ThreadId = "thread_exec_resume_match_001",
                    Name = "thread_target_name",
                    Preview = "matched thread",
                    Cwd = workingDirectory,
                    UpdatedAt = new DateTimeOffset(2026, 3, 26, 10, 0, 0, TimeSpan.Zero),
                },
            ],
        };
        fakeRuntime.ResumeThreadAsyncHandler = (threadId, _) =>
        {
            fakeRuntime.ActiveThreadId = threadId;
            return Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = threadId,
                Preview = "matched thread",
                Cwd = workingDirectory,
                SeedHistory = [],
                Turns = [],
            });
        };
        fakeRuntime.SendAsyncHandler = (userMessage, _, _) =>
        {
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextDelta,
                fakeRuntime.ActiveThreadId,
                "turn_exec_resume_match_001",
                text: "resume ok"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextCompleted,
                fakeRuntime.ActiveThreadId,
                "turn_exec_resume_match_001"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.TurnCompleted,
                fakeRuntime.ActiveThreadId,
                "turn_exec_resume_match_001",
                status: "completed"));
            return Task.FromResult(AgentSendResult.Ok(
                userMessage,
                turnId: "turn_exec_resume_match_001",
                turnStatus: "completed"));
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Contains("resume ok", output, StringComparison.Ordinal);
        var listRequest = Assert.Single(fakeRuntime.ThreadListRequests);
        Assert.Equal("thread_target_name", listRequest.SearchTerm);
        Assert.Empty(fakeRuntime.StartThreadCalls);
        Assert.Collection(fakeRuntime.ResumeThreadCalls, threadId => Assert.Equal("thread_exec_resume_match_001", threadId));
        var sendCall = Assert.Single(fakeRuntime.SendCalls);
        Assert.Equal("继续跟进", sendCall.UserMessage);
    }

    [Fact]
    public async Task RunAsync_ExecReviewUncommitted_WithInjectedRuntime_StartsReviewAndWaitsForTurnCompletion()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ExecCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["exec", "review", "--uncommitted", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.StartNewThreadAsyncHandler = _ =>
        {
            fakeRuntime.ActiveThreadId = "thread_exec_review_source_001";
            return Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
            {
                ThreadId = "thread_exec_review_source_001",
                Preview = "review source thread",
                Cwd = workingDirectory,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        };
        fakeRuntime.StartReviewHandler = request =>
        {
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.TurnStarted,
                request.ThreadId,
                "turn_exec_review_001"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextDelta,
                request.ThreadId,
                "turn_exec_review_001",
                text: "review finished"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextCompleted,
                request.ThreadId,
                "turn_exec_review_001"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.TurnCompleted,
                request.ThreadId,
                "turn_exec_review_001",
                status: "completed"));
            return new ControlPlaneReviewStartResult
            {
                ReviewThreadId = request.ThreadId,
                Turn = new ControlPlaneReviewTurn
                {
                    Id = "turn_exec_review_001",
                    Status = "inProgress",
                    DisplayText = "检查未提交改动",
                },
            };
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Single(fakeRuntime.StartThreadCalls);
        Assert.Empty(fakeRuntime.SendCalls);
        var reviewRequest = Assert.Single(fakeRuntime.ReviewStartRequests);
        Assert.Equal("thread_exec_review_source_001", reviewRequest.ThreadId);
        Assert.IsType<ControlPlaneReviewUncommittedChangesTarget>(reviewRequest.Target);
        Assert.Collection(fakeRuntime.UnsubscribeThreadRequests, threadId => Assert.Equal("thread_exec_review_source_001", threadId));

        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 2);
        Assert.Contains("\"type\":\"turn_completed\"", output, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"exec_completed\"", lines[^1], StringComparison.Ordinal);
        Assert.Contains("\"assistantText\":\"review finished\"", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ExecReviewCustomPrompt_TrimsInstructions_BeforeStartReview()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ExecCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["exec", "review", "  请只看新增风险  "],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.StartNewThreadAsyncHandler = _ =>
        {
            fakeRuntime.ActiveThreadId = "thread_exec_review_custom_001";
            return Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
            {
                ThreadId = "thread_exec_review_custom_001",
                Preview = "custom review thread",
                Cwd = workingDirectory,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        };
        fakeRuntime.StartReviewHandler = request =>
        {
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextDelta,
                request.ThreadId,
                "turn_exec_review_custom_001",
                text: "发现一处潜在问题"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.TurnCompleted,
                request.ThreadId,
                "turn_exec_review_custom_001",
                status: "completed"));
            return new ControlPlaneReviewStartResult
            {
                ReviewThreadId = request.ThreadId,
                Turn = new ControlPlaneReviewTurn
                {
                    Id = "turn_exec_review_custom_001",
                    Status = "inProgress",
                    DisplayText = "请只看新增风险",
                },
            };
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunAsync", command);

        Assert.Equal(0, exitCode);
        var reviewRequest = Assert.Single(fakeRuntime.ReviewStartRequests);
        var customTarget = Assert.IsType<ControlPlaneReviewCustomTarget>(reviewRequest.Target);
        Assert.Equal("请只看新增风险", customTarget.Instructions);
        Assert.Contains("发现一处潜在问题", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ExecReview_WhenCancelled_ReturnsFailure_AndUnsubscribesThread()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ExecCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["exec", "review", "--uncommitted", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.StartNewThreadAsyncHandler = _ =>
        {
            fakeRuntime.ActiveThreadId = "thread_exec_review_cancel_001";
            return Task.FromResult<AgentThreadInfo?>(new AgentThreadInfo
            {
                ThreadId = "thread_exec_review_cancel_001",
                Preview = "cancel review thread",
                Cwd = workingDirectory,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        };
        fakeRuntime.StartReviewHandler = request => new ControlPlaneReviewStartResult
        {
            ReviewThreadId = request.ThreadId,
            Turn = new ControlPlaneReviewTurn
            {
                Id = "turn_exec_review_cancel_001",
                Status = "inProgress",
                DisplayText = "等待取消",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunAsync", command, cancellation.Token);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"type\":\"exec_failed\"", output, StringComparison.Ordinal);
        Assert.Equal(1, fakeRuntime.InterruptCallCount);
        Assert.Collection(fakeRuntime.UnsubscribeThreadRequests, threadId => Assert.Equal("thread_exec_review_cancel_001", threadId));
    }

    [Fact]
    public async Task RunCodeModeAsync_Wait_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["code-mode", "wait", "--thread-id", "thread_code_wait_001", "--cell-id", "cell_code_wait_001", "--yield-time-ms", "400", "--max-tokens", "32", "--terminate"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime();
        fakeRuntime.InvokeDiagnosticRpcHandler = static (method, _) => method switch
        {
            "exec_wait" => StructuredJson(
                """
                {
                  "success": true,
                  "status": "terminated",
                  "threadId": "thread_code_wait_001",
                  "turnId": "turn_code_wait_001",
                  "cellId": "cell_code_wait_001",
                  "output": "terminated by user"
                }
                """),
            _ => throw new InvalidOperationException($"unexpected method: {method}"),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunCodeModeAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var invocation = Assert.Single(fakeRuntime.Invocations);
        Assert.Equal("exec_wait", invocation.Method);
        Assert.NotNull(invocation.Parameters);
        Assert.Equal("thread_code_wait_001", invocation.Parameters!.GetProperty("threadId").GetString());
        Assert.Equal("cell_code_wait_001", invocation.Parameters.GetProperty("cellId").GetString());
        Assert.Equal(400, invocation.Parameters.GetProperty("yieldTimeMs").GetInt32());
        Assert.Equal(32, invocation.Parameters.GetProperty("maxTokens").GetInt32());
        Assert.True(invocation.Parameters.GetProperty("terminate").GetBoolean());
        Assert.Contains("status=terminated", output, StringComparison.Ordinal);
        Assert.Contains("terminated by user", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ConfigRequirementsRead_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["config", "requirements"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadConfigRequirementsHandler = _ => new ControlPlaneConfigRequirementsResult
            {
                IsDefined = true,
                AllowedApprovalPolicies = ["never"],
                FeatureRequirements = new Dictionary<string, bool>
                {
                    ["tool_search"] = true,
                },
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.ConfigRequirementsReadRequests);
        Assert.Equal(workingDirectory, request.WorkingDirectory);
        Assert.Contains("\"allowedApprovalPolicies\": [", output, StringComparison.Ordinal);
        Assert.Contains("\"never\"", output, StringComparison.Ordinal);
        Assert.Contains("\"tool_search\": true", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ConfigRead_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["config", "read", "--include-layers"],
            workingDirectory,
            configPath);
        var serializedWorkingDirectory = JsonSerializer.Serialize(workingDirectory);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadConfigHandler = _ => new ControlPlaneConfigSnapshotResult
            {
                Config = StructuredValueTestHelper.FromJson(
                    $$"""
                    {
                      "model": "claude-3-7-sonnet",
                      "personality": "pragmatic",
                      "plan_mode_reasoning_effort": "medium",
                      "experimental_instructions_file": "D:/legacy/instructions.md",
                      "plugins": {
                        "demo_plugin": {
                          "enabled": false
                        }
                      },
                      "project_root_markers": [".git", ".hg"],
                      "windows_wsl_setup_acknowledged": true,
                      "zsh_path": "C:/Program Files/Git/usr/bin/zsh.exe",
                      "apps": {
                        "_default": {
                          "enabled": true,
                          "default_tools_enabled": true
                        },
                        "demo": {
                          "enabled": true,
                          "isAccessible": true,
                          "default_tools_enabled": true,
                          "default_tools_approval_mode": "prompt",
                          "tools": {
                            "search": {
                              "enabled": true,
                              "approval_mode": "auto"
                            }
                          }
                        }
                      },
                      "audio": {
                        "microphone": "default",
                        "speaker": "headphones"
                      },
                      "feedback": {
                        "enabled": false
                      },
                      "file_opener": "vscode",
                      "ghost_snapshot": {
                        "disable_warnings": true,
                        "ignore_large_untracked_dirs": 128,
                        "ignore_large_untracked_files": 64
                      },
                      "history": {
                        "persistence": "save-all",
                        "max_bytes": 2048
                      },
                      "memories": {
                        "no_memories_if_mcp_or_web_search": true,
                        "generate_memories": false,
                        "use_memories": false,
                        "max_raw_memories_for_consolidation": 512,
                        "max_unused_days": 21,
                        "max_rollout_age_days": 42,
                        "max_rollouts_per_startup": 9,
                        "min_rollout_idle_hours": 24,
                        "extract_model": "gpt-5-mini",
                        "consolidation_model": "gpt-5"
                      },
                      "features": {
                        "js_repl": true,
                        "plugins": false
                      },
                      "providers": {
                        "demo": {
                          "name": "Demo Provider",
                          "base_url": "https://example.invalid/v1",
                          "default_protocol": "responses",
                          "supports_websockets": true
                        }
                      },
                      "profiles": {
                        "default": {
                          "sandbox_mode": "workspace-write",
                          "experimental_instructions_file": "D:/legacy/profile-instructions.md",
                          "plan_mode_reasoning_effort": "high",
                          "features": {
                            "code_mode": true
                          },
                          "windows": {
                            "sandbox": "unelevated"
                          }
                        }
                      },
                      "shell_environment_policy": {
                        "inherit": "core",
                        "ignore_default_excludes": false,
                        "exclude": ["*_SECRET"],
                        "set": {
                          "FOO": "BAR"
                        },
                        "include_only": ["PATH"],
                        "experimental_use_profile": true
                      },
                      "windows": {
                        "sandbox": "elevated"
                      },
                      "mcp_servers": {
                        "docs": {
                          "url": "https://example.invalid/mcp",
                          "enabled": true,
                          "enabled_tools": ["search"],
                          "startup_timeout_sec": 12.5
                        }
                      },
                      "notice": {
                        "hide_full_access_warning": true,
                        "hide_world_writable_warning": false,
                        "hide_rate_limit_model_nudge": true,
                        "hide_gpt5_1_migration_prompt": true,
                        "hide_gpt-5.1-codex-max_migration_prompt": false,
                        "model_migrations": {
                          "gpt-5.1": "gpt-5.4"
                        }
                      },
                      "otel": {
                        "log_user_prompt": true,
                        "environment": "test",
                        "exporter": {
                          "otlp-http": {
                            "endpoint": "https://otel.invalid/http",
                            "headers": {
                              "Authorization": "Bearer test"
                            },
                            "protocol": "json",
                            "tls": {
                              "ca_certificate": "D:/certs/ca.pem"
                            }
                          }
                        },
                        "trace_exporter": "none",
                        "metrics_exporter": "statsig"
                      },
                      "projects": {
                        "D:/Work/TianShu/Test": {
                          "trust_level": "trusted"
                        }
                      },
                      "sandbox_workspace_write": {
                        "network_access": true,
                        "writable_roots": [{{serializedWorkingDirectory}}]
                      },
                      "permissions": {
                        "trusted": {
                          "filesystem": {
                            "/workspace": "write",
                            "/repo": {
                              ".": "read"
                            }
                          },
                          "network": {
                            "enabled": true,
                            "allowed_domains": ["example.invalid"]
                          }
                        }
                      },
                      "skills": {
                        "bundled": {
                          "enabled": false
                        },
                        "config": [
                          {
                            "path": "D:/Work/TianShu/.tianshu/skills/demo-search",
                            "enabled": true
                          }
                        ]
                      },
                      "tui": {
                        "notifications": ["toast"],
                        "notification_method": "auto",
                        "animations": false,
                        "show_tooltips": true,
                        "alternate_screen": "never",
                        "status_line": ["model", "cwd"],
                        "theme": "ansi",
                        "model_availability_nux": {
                          "gpt-5.4": 2
                        }
                      },
                      "agents": {
                        "max_threads": 4,
                        "max_depth": 6,
                        "job_max_runtime_seconds": 180,
                        "researcher": {
                          "description": "Research role",
                          "config_file": "./agents/researcher.toml",
                          "nickname_candidates": ["Hypatia", "Noether"]
                        }
                      }
                    }
                    """),
                Fields =
                [
                    new ControlPlaneConfigField
                    {
                        KeyPath = "legacy_only_key",
                        ValueKind = "String",
                        ValueText = "legacy-value",
                        Value = StructuredValue.FromString("legacy-value"),
                        SourceType = "project",
                        SourcePath = configPath,
                        SourceText = $"project · {configPath}",
                    },
                ],
                Origins = new Dictionary<string, ControlPlaneConfigOrigin>(StringComparer.Ordinal)
                {
                    ["model"] = new ControlPlaneConfigOrigin
                    {
                        Type = "project",
                        File = configPath,
                        Version = "origin-v1",
                    },
                    ["sandbox_workspace_write"] = new ControlPlaneConfigOrigin
                    {
                        Type = "project",
                        File = configPath,
                    },
                },
                Layers =
                [
                    new ControlPlaneConfigLayer
                    {
                        Name = StructuredValue.FromString("project"),
                        Version = "1",
                        Config = StructuredValueTestHelper.FromJson(
                            """
                            {
                              "model": "claude-3-7-sonnet"
                            }
                            """),
                        DisabledReason = "readonly",
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.ConfigReadRequests);
        Assert.Equal(workingDirectory, request.WorkingDirectory);
        Assert.True(request.IncludeLayers);
        using var document = JsonDocument.Parse(output);
        Assert.Equal("claude-3-7-sonnet", document.RootElement.GetProperty("config").GetProperty("model").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("sandbox_workspace_write").GetProperty("network_access").GetBoolean());
        Assert.Equal(workingDirectory, document.RootElement.GetProperty("config").GetProperty("sandbox_workspace_write").GetProperty("writable_roots")[0].GetString());
        Assert.Equal("pragmatic", document.RootElement.GetProperty("config").GetProperty("personality").GetString());
        Assert.Equal("medium", document.RootElement.GetProperty("config").GetProperty("plan_mode_reasoning_effort").GetString());
        Assert.Equal("D:/legacy/instructions.md", document.RootElement.GetProperty("config").GetProperty("experimental_instructions_file").GetString());
        Assert.False(document.RootElement.GetProperty("config").GetProperty("plugins").GetProperty("demo_plugin").GetProperty("enabled").GetBoolean());
        Assert.Equal(".git", document.RootElement.GetProperty("config").GetProperty("project_root_markers")[0].GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("windows_wsl_setup_acknowledged").GetBoolean());
        Assert.Equal("C:/Program Files/Git/usr/bin/zsh.exe", document.RootElement.GetProperty("config").GetProperty("zsh_path").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("apps").GetProperty("_default").GetProperty("default_tools_enabled").GetBoolean());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("apps").GetProperty("demo").GetProperty("enabled").GetBoolean());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("apps").GetProperty("demo").GetProperty("isAccessible").GetBoolean());
        Assert.Equal("prompt", document.RootElement.GetProperty("config").GetProperty("apps").GetProperty("demo").GetProperty("default_tools_approval_mode").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("apps").GetProperty("demo").GetProperty("tools").GetProperty("search").GetProperty("enabled").GetBoolean());
        Assert.Equal("auto", document.RootElement.GetProperty("config").GetProperty("apps").GetProperty("demo").GetProperty("tools").GetProperty("search").GetProperty("approval_mode").GetString());
        Assert.Equal("default", document.RootElement.GetProperty("config").GetProperty("audio").GetProperty("microphone").GetString());
        Assert.Equal("headphones", document.RootElement.GetProperty("config").GetProperty("audio").GetProperty("speaker").GetString());
        Assert.False(document.RootElement.GetProperty("config").GetProperty("feedback").GetProperty("enabled").GetBoolean());
        Assert.Equal("vscode", document.RootElement.GetProperty("config").GetProperty("file_opener").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("ghost_snapshot").GetProperty("disable_warnings").GetBoolean());
        Assert.Equal(128, document.RootElement.GetProperty("config").GetProperty("ghost_snapshot").GetProperty("ignore_large_untracked_dirs").GetInt64());
        Assert.Equal(64, document.RootElement.GetProperty("config").GetProperty("ghost_snapshot").GetProperty("ignore_large_untracked_files").GetInt64());
        Assert.Equal("save-all", document.RootElement.GetProperty("config").GetProperty("history").GetProperty("persistence").GetString());
        Assert.Equal(2048, document.RootElement.GetProperty("config").GetProperty("history").GetProperty("max_bytes").GetInt64());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("memories").GetProperty("no_memories_if_mcp_or_web_search").GetBoolean());
        Assert.False(document.RootElement.GetProperty("config").GetProperty("memories").GetProperty("generate_memories").GetBoolean());
        Assert.False(document.RootElement.GetProperty("config").GetProperty("memories").GetProperty("use_memories").GetBoolean());
        Assert.Equal(512, document.RootElement.GetProperty("config").GetProperty("memories").GetProperty("max_raw_memories_for_consolidation").GetInt64());
        Assert.Equal(21, document.RootElement.GetProperty("config").GetProperty("memories").GetProperty("max_unused_days").GetInt64());
        Assert.Equal(42, document.RootElement.GetProperty("config").GetProperty("memories").GetProperty("max_rollout_age_days").GetInt64());
        Assert.Equal(9, document.RootElement.GetProperty("config").GetProperty("memories").GetProperty("max_rollouts_per_startup").GetInt64());
        Assert.Equal(24, document.RootElement.GetProperty("config").GetProperty("memories").GetProperty("min_rollout_idle_hours").GetInt64());
        Assert.Equal("gpt-5-mini", document.RootElement.GetProperty("config").GetProperty("memories").GetProperty("extract_model").GetString());
        Assert.Equal("gpt-5", document.RootElement.GetProperty("config").GetProperty("memories").GetProperty("consolidation_model").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("features").GetProperty("js_repl").GetBoolean());
        Assert.False(document.RootElement.GetProperty("config").GetProperty("features").GetProperty("plugins").GetBoolean());
        Assert.Equal("Demo Provider", document.RootElement.GetProperty("config").GetProperty("providers").GetProperty("demo").GetProperty("name").GetString());
        Assert.Equal("https://example.invalid/v1", document.RootElement.GetProperty("config").GetProperty("providers").GetProperty("demo").GetProperty("base_url").GetString());
        Assert.Equal("responses", document.RootElement.GetProperty("config").GetProperty("providers").GetProperty("demo").GetProperty("default_protocol").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("providers").GetProperty("demo").GetProperty("supports_websockets").GetBoolean());
        Assert.Equal("workspace-write", document.RootElement.GetProperty("config").GetProperty("profiles").GetProperty("default").GetProperty("sandbox_mode").GetString());
        Assert.Equal("D:/legacy/profile-instructions.md", document.RootElement.GetProperty("config").GetProperty("profiles").GetProperty("default").GetProperty("experimental_instructions_file").GetString());
        Assert.Equal("high", document.RootElement.GetProperty("config").GetProperty("profiles").GetProperty("default").GetProperty("plan_mode_reasoning_effort").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("profiles").GetProperty("default").GetProperty("features").GetProperty("code_mode").GetBoolean());
        Assert.Equal("core", document.RootElement.GetProperty("config").GetProperty("shell_environment_policy").GetProperty("inherit").GetString());
        Assert.False(document.RootElement.GetProperty("config").GetProperty("shell_environment_policy").GetProperty("ignore_default_excludes").GetBoolean());
        Assert.Equal("BAR", document.RootElement.GetProperty("config").GetProperty("shell_environment_policy").GetProperty("set").GetProperty("FOO").GetString());
        Assert.Equal("elevated", document.RootElement.GetProperty("config").GetProperty("windows").GetProperty("sandbox").GetString());
        Assert.Equal("unelevated", document.RootElement.GetProperty("config").GetProperty("profiles").GetProperty("default").GetProperty("windows").GetProperty("sandbox").GetString());
        Assert.Equal("https://example.invalid/mcp", document.RootElement.GetProperty("config").GetProperty("mcp_servers").GetProperty("docs").GetProperty("url").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("mcp_servers").GetProperty("docs").GetProperty("enabled").GetBoolean());
        Assert.Equal("search", document.RootElement.GetProperty("config").GetProperty("mcp_servers").GetProperty("docs").GetProperty("enabled_tools")[0].GetString());
        Assert.Equal(12.5d, document.RootElement.GetProperty("config").GetProperty("mcp_servers").GetProperty("docs").GetProperty("startup_timeout_sec").GetDouble());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("notice").GetProperty("hide_full_access_warning").GetBoolean());
        Assert.False(document.RootElement.GetProperty("config").GetProperty("notice").GetProperty("hide_world_writable_warning").GetBoolean());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("notice").GetProperty("hide_rate_limit_model_nudge").GetBoolean());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("notice").GetProperty("hide_gpt5_1_migration_prompt").GetBoolean());
        Assert.False(document.RootElement.GetProperty("config").GetProperty("notice").GetProperty("hide_gpt-5.1-codex-max_migration_prompt").GetBoolean());
        Assert.Equal("gpt-5.4", document.RootElement.GetProperty("config").GetProperty("notice").GetProperty("model_migrations").GetProperty("gpt-5.1").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("otel").GetProperty("log_user_prompt").GetBoolean());
        Assert.Equal("test", document.RootElement.GetProperty("config").GetProperty("otel").GetProperty("environment").GetString());
        Assert.Equal("https://otel.invalid/http", document.RootElement.GetProperty("config").GetProperty("otel").GetProperty("exporter").GetProperty("otlp-http").GetProperty("endpoint").GetString());
        Assert.Equal("Bearer test", document.RootElement.GetProperty("config").GetProperty("otel").GetProperty("exporter").GetProperty("otlp-http").GetProperty("headers").GetProperty("Authorization").GetString());
        Assert.Equal("json", document.RootElement.GetProperty("config").GetProperty("otel").GetProperty("exporter").GetProperty("otlp-http").GetProperty("protocol").GetString());
        Assert.Equal("D:/certs/ca.pem", document.RootElement.GetProperty("config").GetProperty("otel").GetProperty("exporter").GetProperty("otlp-http").GetProperty("tls").GetProperty("ca_certificate").GetString());
        Assert.Equal("none", document.RootElement.GetProperty("config").GetProperty("otel").GetProperty("trace_exporter").GetString());
        Assert.Equal("statsig", document.RootElement.GetProperty("config").GetProperty("otel").GetProperty("metrics_exporter").GetString());
        Assert.Equal("trusted", document.RootElement.GetProperty("config").GetProperty("projects").GetProperty("D:/Work/TianShu/Test").GetProperty("trust_level").GetString());
        Assert.Equal("legacy-value", document.RootElement.GetProperty("config").GetProperty("legacy_only_key").GetString());
        Assert.Equal("write", document.RootElement.GetProperty("config").GetProperty("permissions").GetProperty("trusted").GetProperty("filesystem").GetProperty("/workspace").GetString());
        Assert.Equal("read", document.RootElement.GetProperty("config").GetProperty("permissions").GetProperty("trusted").GetProperty("filesystem").GetProperty("/repo").GetProperty(".").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("permissions").GetProperty("trusted").GetProperty("network").GetProperty("enabled").GetBoolean());
        Assert.Equal("example.invalid", document.RootElement.GetProperty("config").GetProperty("permissions").GetProperty("trusted").GetProperty("network").GetProperty("allowed_domains")[0].GetString());
        Assert.False(document.RootElement.GetProperty("config").GetProperty("skills").GetProperty("bundled").GetProperty("enabled").GetBoolean());
        Assert.Equal("D:/Work/TianShu/.tianshu/skills/demo-search", document.RootElement.GetProperty("config").GetProperty("skills").GetProperty("config")[0].GetProperty("path").GetString());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("skills").GetProperty("config")[0].GetProperty("enabled").GetBoolean());
        Assert.Equal("toast", document.RootElement.GetProperty("config").GetProperty("tui").GetProperty("notifications")[0].GetString());
        Assert.Equal("auto", document.RootElement.GetProperty("config").GetProperty("tui").GetProperty("notification_method").GetString());
        Assert.False(document.RootElement.GetProperty("config").GetProperty("tui").GetProperty("animations").GetBoolean());
        Assert.True(document.RootElement.GetProperty("config").GetProperty("tui").GetProperty("show_tooltips").GetBoolean());
        Assert.Equal("never", document.RootElement.GetProperty("config").GetProperty("tui").GetProperty("alternate_screen").GetString());
        Assert.Equal("model", document.RootElement.GetProperty("config").GetProperty("tui").GetProperty("status_line")[0].GetString());
        Assert.Equal("ansi", document.RootElement.GetProperty("config").GetProperty("tui").GetProperty("theme").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("config").GetProperty("tui").GetProperty("model_availability_nux").GetProperty("gpt-5.4").GetInt64());
        Assert.Equal(4, document.RootElement.GetProperty("config").GetProperty("agents").GetProperty("max_threads").GetInt64());
        Assert.Equal(6, document.RootElement.GetProperty("config").GetProperty("agents").GetProperty("max_depth").GetInt32());
        Assert.Equal(180, document.RootElement.GetProperty("config").GetProperty("agents").GetProperty("job_max_runtime_seconds").GetInt64());
        Assert.Equal("Research role", document.RootElement.GetProperty("config").GetProperty("agents").GetProperty("researcher").GetProperty("description").GetString());
        Assert.Equal("./agents/researcher.toml", document.RootElement.GetProperty("config").GetProperty("agents").GetProperty("researcher").GetProperty("config_file").GetString());
        Assert.Equal("Hypatia", document.RootElement.GetProperty("config").GetProperty("agents").GetProperty("researcher").GetProperty("nickname_candidates")[0].GetString());
        var origin = document.RootElement.GetProperty("origins").GetProperty("model").GetProperty("name");
        Assert.Equal("project", origin.GetProperty("type").GetString());
        Assert.Equal(configPath, origin.GetProperty("file").GetString());
        Assert.Equal("origin-v1", document.RootElement.GetProperty("origins").GetProperty("model").GetProperty("version").GetString());
        var sandboxOrigin = document.RootElement.GetProperty("origins").GetProperty("sandbox_workspace_write").GetProperty("name");
        Assert.Equal("project", sandboxOrigin.GetProperty("type").GetString());
        Assert.Equal(configPath, sandboxOrigin.GetProperty("file").GetString());
        var layer = Assert.Single(document.RootElement.GetProperty("layers").EnumerateArray());
        Assert.Equal("project", layer.GetProperty("name").GetString());
        Assert.Equal("1", layer.GetProperty("version").GetString());
        Assert.Equal("readonly", layer.GetProperty("disabledReason").GetString());
        Assert.Equal("claude-3-7-sonnet", layer.GetProperty("config").GetProperty("model").GetString());
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ModelList_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["model-route", "list", "--limit", "2", "--cursor", "cursor_001", "--include-hidden"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListModelsHandler = _ => new ControlPlaneModelCatalogResult
            {
                NextCursor = "cursor_002",
                Items =
                [
                    new ControlPlaneModelCatalogItem
                    {
                        Id = "gpt-5",
                        DisplayName = "GPT-5",
                        Model = "gpt-5",
                        DefaultReasoningEffort = "high",
                    },
                    new ControlPlaneModelCatalogItem
                    {
                        Id = "gpt-5-mini",
                        DisplayName = "GPT-5 Mini",
                        Model = "gpt-5-mini",
                        DefaultReasoningEffort = "medium",
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.ModelListRequests);
        Assert.Equal(2, request.Limit);
        Assert.Equal("cursor_001", request.Cursor);
        Assert.True(request.IncludeHidden);
        Assert.Contains("gpt-5\tGPT-5\tgpt-5\thigh", output, StringComparison.Ordinal);
        Assert.Contains("gpt-5-mini\tGPT-5 Mini\tgpt-5-mini\tmedium", output, StringComparison.Ordinal);
        Assert.Contains("nextCursor\tcursor_002", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_CatalogAndAgentFormalQueries_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var catalogCommand = ParseCommandWithWorkspace(
            parserType,
            ["model-route", "catalog", "--limit", "3", "--include-hidden", "--json"],
            workingDirectory,
            configPath);
        var resolveCommand = ParseCommandWithWorkspace(
            parserType,
            ["model-route", "resolve", "--provider-key", "openai", "--model-key", "gpt-5", "--reasoning-effort", "high", "--reasoning-summary", "detailed", "--verbosity", "verbose", "--prefer-websocket-transport", "--json"],
            workingDirectory,
            configPath);
        var agentListCommand = ParseCommandWithWorkspace(
            parserType,
            ["agent", "list", "--limit", "4", "--cursor", "agent_cursor_001", "--include-primary-threads", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListProviderProfilesHandler = request => new CapabilityCatalogSnapshot(
                activeProviderKey: "openai",
                activeModel: "gpt-5",
                providers:
                [
                    new ProviderProfile(
                        "openai",
                        "OpenAI",
                        "responses",
                        transportModes: ["http", "websocket"],
                        models:
                        [
                            new ModelProfile(
                                "gpt-5",
                                "gpt-5",
                                "GPT-5",
                                defaultReasoningEffort: "high",
                                supportsReasoningSummaries: true,
                                defaultReasoningSummary: "detailed",
                                supportsVerbosity: true,
                                defaultVerbosity: "verbose",
                                preferWebsocketTransport: true),
                        ],
                        supportsWebsockets: true),
                ]),
            ResolveEngineBindingHandler = request => new ResolvedEngineBinding(
                new EngineBinding(
                    "openai:gpt-5",
                    request.PreferredProviderKey ?? "openai",
                    request.PreferredModelKey ?? "gpt-5",
                    "gpt-5",
                    "responses",
                    new CatalogStreamingPreference("websocket", request.PreferWebsocketTransport, useWebsocketTransport: true),
                    supportsWebsockets: true,
                    reasoning: new CatalogReasoningProfile(
                        request.ReasoningEffort,
                        request.ReasoningSummary,
                        request.Verbosity))),
            ListAgentsHandler = request => new ControlPlaneAgentRosterResult
            {
                NextCursor = "agent_cursor_002",
                Agents =
                [
                    new ControlPlaneAgentDescriptor
                    {
                        ThreadId = new ThreadId("agent-catalog-cli-001"),
                        AgentNickname = "Hermes",
                        AgentRole = request.IncludePrimaryThreads ? "primary" : "worker",
                        Preview = "Catalog/Agent mirror",
                        UpdatedAt = DateTimeOffset.UtcNow,
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (catalogExitCode, catalogOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", catalogCommand);
        var (resolveExitCode, resolveOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", resolveCommand);
        var (agentListExitCode, agentListOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", agentListCommand);

        Assert.Equal(0, catalogExitCode);
        Assert.Equal(0, resolveExitCode);
        Assert.Equal(0, agentListExitCode);

        var catalogRequest = Assert.Single(fakeRuntime.ProviderCatalogRequests);
        Assert.Equal(workingDirectory, catalogRequest.WorkspacePath);
        Assert.True(catalogRequest.IncludeHiddenModels);
        Assert.Equal(3, catalogRequest.ModelLimit);

        var resolveRequest = Assert.Single(fakeRuntime.EngineBindingRequests);
        Assert.Equal(workingDirectory, resolveRequest.WorkspacePath);
        Assert.Equal("openai", resolveRequest.PreferredProviderKey);
        Assert.Equal("gpt-5", resolveRequest.PreferredModelKey);
        Assert.Equal("high", resolveRequest.ReasoningEffort);
        Assert.Equal("detailed", resolveRequest.ReasoningSummary);
        Assert.Equal("verbose", resolveRequest.Verbosity);
        Assert.True(resolveRequest.PreferWebsocketTransport);

        var agentListRequest = Assert.Single(fakeRuntime.AgentListRequests);
        Assert.Equal(4, agentListRequest.Limit);
        Assert.Equal("agent_cursor_001", agentListRequest.Cursor);
        Assert.True(agentListRequest.IncludePrimaryThreads);

        Assert.Contains("OpenAI", catalogOutput, StringComparison.Ordinal);
        Assert.Contains("\"providerKey\": \"openai\"", resolveOutput, StringComparison.Ordinal);
        Assert.Contains("Hermes", agentListOutput, StringComparison.Ordinal);
        Assert.Contains("agent_cursor_002", agentListOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ToolsList_WithInjectedRuntime_UsesCapabilityCatalogTools()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var command = ParseCommandWithWorkspace(
            parserType,
            ["tools", "list", "--include-hidden"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListProviderProfilesHandler = request => new CapabilityCatalogSnapshot(
                tools: new ResolvedToolCatalogSnapshot(
                [
                    new ResolvedToolCatalogItem(
                        "grep_files",
                        "Search files.",
                        ToolImplementationKind.Managed,
                        available: true,
                        modelVisible: true,
                        requirements:
                        [
                            new ToolRuntimeRequirement("file_system", "File system"),
                        ]),
                    new ResolvedToolCatalogItem(
                        "shell_command",
                        "Run shell command.",
                        ToolImplementationKind.Unavailable,
                        available: false,
                        modelVisible: false,
                        reason: "powershell unavailable",
                        requirements:
                        [
                            new ToolRuntimeRequirement("powershell", "PowerShell"),
                        ]),
                ])),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        var request = Assert.Single(fakeRuntime.ProviderCatalogRequests);
        Assert.Equal(workingDirectory, request.WorkspacePath);
        Assert.True(request.IncludeHiddenTools);
        Assert.Contains("grep_files\t-\tManaged\tyes\tyes\tfile_system\t-\t-", output, StringComparison.Ordinal);
        Assert.Contains("shell_command\t-\tUnavailable\tno\tno\tpowershell\t-\tpowershell unavailable", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ToolsExportConfig_WritesTemplateFromCapabilityCatalog()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var outputPath = Path.Combine(workingDirectory, "tool_profiles.builtin.toml");
        var command = ParseCommandWithWorkspace(
            parserType,
            ["tools", "export-config", "--out", outputPath],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListProviderProfilesHandler = request => new CapabilityCatalogSnapshot(
                tools: new ResolvedToolCatalogSnapshot(
                [
                    new ResolvedToolCatalogItem(
                        "read_file",
                        "Read file.",
                        ToolImplementationKind.Managed,
                        available: true,
                        modelVisible: true,
                        implementationId: "read_file"),
                ])),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        var request = Assert.Single(fakeRuntime.ProviderCatalogRequests);
        Assert.True(request.IncludeHiddenTools);
        Assert.Contains("工具配置模板已导出", output, StringComparison.Ordinal);
        var toml = File.ReadAllText(outputPath);
        Assert.Contains("[tool_profiles.builtin]", toml, StringComparison.Ordinal);
        Assert.Contains("[tools.read_file]", toml, StringComparison.Ordinal);
        Assert.Contains("implementation_id = \"read_file\"", toml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_AppList_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["app", "list", "--limit", "5", "--cursor", "cursor_app_001", "--thread-id", "thread_app_runtime", "--force-refetch"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListAppsHandler = _ => new ControlPlaneAppCatalogResult
            {
                NextCursor = "cursor_app_002",
                Items =
                [
                    new ControlPlaneAppDescriptor
                    {
                        Id = "connector_example",
                        Name = "Example Connector",
                        IsEnabled = true,
                        IsAccessible = false,
                        PluginDisplayNames = ["demo-plugin", "demo-plugin-2"],
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.AppListRequests);
        Assert.Equal(5, request.Limit);
        Assert.Equal("cursor_app_001", request.Cursor);
        Assert.Equal("thread_app_runtime", request.ThreadId);
        Assert.True(request.ForceRefetch);

        Assert.Contains("connector_example\tenabled=True\taccessible=False\tplugins=demo-plugin,demo-plugin-2", output, StringComparison.Ordinal);
        Assert.Contains("nextCursor\tcursor_app_002", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_SessionQueries_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var collaborationSpace = new CollaborationSpaceRef(new CollaborationSpaceId("space-cli-001"), "space-cli-001", "CLI Space");

        var overviewCommand = ParseCommandWithWorkspace(
            parserType,
            ["session", "overview", "--session-id", "session-cli-001", "--json"],
            workingDirectory,
            configPath);

        var snapshotCommand = ParseCommandWithWorkspace(
            parserType,
            ["session", "snapshot", "--json"],
            workingDirectory,
            configPath);

        var listCommand = ParseCommandWithWorkspace(
            parserType,
            ["session", "list", "--collaboration-space-id", "space-cli-001", "--include-closed", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetSessionOverviewHandler = query => new SessionOverviewProjection(
                query.SessionId,
                "CLI Session",
                collaborationSpace,
                SessionMode.Review,
                new ThreadId("thread-session-cli"),
                HasActiveTurn: true),
            ListSessionsHandler = query =>
            [
                new SessionOverviewProjection(
                    new SessionId("session-cli-list"),
                    "CLI Session List",
                    collaborationSpace,
                    SessionMode.Interactive,
                    IsClosed: query.IncludeClosed),
            ],
        };
        fakeRuntime.ActiveThreadId = "thread-session-snapshot-cli";

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (snapshotExitCode, snapshotOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", snapshotCommand);
        var (overviewExitCode, overviewOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", overviewCommand);
        var (listExitCode, listOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", listCommand);

        Assert.Equal(0, snapshotExitCode);
        Assert.Equal(0, overviewExitCode);
        Assert.Equal(0, listExitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var overviewRequest = Assert.Single(fakeRuntime.SessionOverviewRequests);
        Assert.Equal("session-cli-001", overviewRequest.SessionId.Value);
        var listRequest = Assert.Single(fakeRuntime.SessionListRequests);
        Assert.Equal("space-cli-001", listRequest.CollaborationSpaceId?.Value);
        Assert.True(listRequest.IncludeClosed);

        Assert.Contains("thread-session-snapshot-cli", snapshotOutput, StringComparison.Ordinal);
        Assert.Contains("session-cli-001", overviewOutput, StringComparison.Ordinal);
        Assert.Contains("CLI Session", overviewOutput, StringComparison.Ordinal);
        Assert.Contains("session-cli-list", listOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ConversationQuery_WithInjectedRuntime_UseFormalRequest()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var collaborationSpace = new CollaborationSpaceRef(new CollaborationSpaceId("space-conversation-cli"), "space-conversation-cli", "Conversation CLI");

        var readCommand = ParseCommandWithWorkspace(
            parserType,
            ["conversation", "read", "--thread-id", "thread-conversation-cli", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetThreadProjectionHandler = query => new TianShu.Contracts.Projections.ThreadProjection(
                query.ThreadId,
                "Conversation CLI Thread",
                collaborationSpace,
                new ParticipantRef(new ParticipantId("participant-conversation-cli"), ParticipantKind.Human, "Operator"),
                new TurnId("turn-conversation-cli"),
                HasActiveTurn: true),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", readCommand);

        Assert.Equal(0, exitCode);
        Assert.Equal("thread-conversation-cli", Assert.Single(fakeRuntime.ThreadProjectionRequests).ThreadId.Value);
        Assert.Contains("Conversation CLI Thread", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_GovernanceQueries_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var requestedFrom = new ParticipantRef(new ParticipantId("participant-governance-cli"), ParticipantKind.Human, "Approver");

        var approvalsCommand = ParseCommandWithWorkspace(
            parserType,
            ["governance", "approvals", "--requested-from-participant-id", requestedFrom.Id.Value],
            workingDirectory,
            configPath);
        var userInputsCommand = ParseCommandWithWorkspace(
            parserType,
            ["governance", "user-inputs", "--participant-id", requestedFrom.Id.Value],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetApprovalQueueProjectionHandler = _ => new TianShu.Contracts.Projections.ApprovalQueueProjection(
            [
                new TianShu.Contracts.Projections.ApprovalQueueItem(
                    new ApprovalId("approval-governance-cli"),
                    "Need approval",
                    "command execution",
                    requestedFrom,
                    DateTimeOffset.UtcNow),
            ]),
            ListUserInputRequestsHandler = _ =>
            [
                new UserInputRequest(
                    new UserInputRequestId("user-input-governance-cli"),
                    "Please confirm target branch",
                    requestedFrom,
                    requestedAt: DateTimeOffset.UtcNow),
            ],
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (approvalsExitCode, approvalsOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", approvalsCommand);
        var (userInputsExitCode, userInputsOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", userInputsCommand);

        Assert.Equal(0, approvalsExitCode);
        Assert.Equal(0, userInputsExitCode);
        Assert.Equal(requestedFrom.Id.Value, Assert.Single(fakeRuntime.ApprovalQueueProjectionRequests).RequestedFromParticipantId?.Value);
        Assert.Equal(requestedFrom.Id.Value, Assert.Single(fakeRuntime.UserInputListRequests).RequestedFromParticipantId?.Value);
        Assert.Contains("待审批请求：1 条", approvalsOutput, StringComparison.Ordinal);
        Assert.Contains("approval-governance-cli", approvalsOutput, StringComparison.Ordinal);
        Assert.Contains("待补充输入请求：1 条", userInputsOutput, StringComparison.Ordinal);
        Assert.Contains("user-input-governance-cli", userInputsOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ArtifactQueries_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var collaborationSpace = new CollaborationSpaceRef(new CollaborationSpaceId("space-artifact-cli"), "space-artifact-cli", "Artifact CLI");
        var producedBy = new ParticipantRef(new ParticipantId("participant-artifact-cli"), ParticipantKind.Agent, "Builder");

        var readCommand = ParseCommandWithWorkspace(
            parserType,
            ["artifact", "read", "--artifact-id", "artifact-cli-001", "--json"],
            workingDirectory,
            configPath);
        var listCommand = ParseCommandWithWorkspace(
            parserType,
            ["artifact", "list", "--space-id", collaborationSpace.Id.Value, "--produced-by-participant-id", producedBy.Id.Value, "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetArtifactProjectionHandler = query => new ArtifactProjection(
                query.ArtifactId,
                "Artifact CLI Detail",
                ArtifactKind.Document,
                ArtifactLifecycleState.Published,
                collaborationSpace,
                producedBy),
            GetArtifactCollectionProjectionHandler = query => new ArtifactCollectionProjection(
                collaborationSpace,
                [
                    new TianShu.Contracts.Projections.ArtifactCollectionItem(
                        new ArtifactId("artifact-cli-list-001"),
                        "Artifact CLI List",
                        ArtifactKind.Document,
                        ArtifactLifecycleState.Published,
                        producedBy,
                        UpdatedAt: DateTimeOffset.UtcNow),
                ]),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (readExitCode, readOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", readCommand);
        var (listExitCode, listOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", listCommand);

        Assert.Equal(0, readExitCode);
        Assert.Equal(0, listExitCode);
        Assert.Equal("artifact-cli-001", Assert.Single(fakeRuntime.ArtifactProjectionRequests).ArtifactId.Value);
        var artifactListRequest = Assert.Single(fakeRuntime.ArtifactCollectionProjectionRequests);
        Assert.Equal(collaborationSpace.Id.Value, artifactListRequest.CollaborationSpaceId?.Value);
        Assert.Equal(producedBy.Id.Value, artifactListRequest.ProducedByParticipantId?.Value);
        Assert.Contains("Artifact CLI Detail", readOutput, StringComparison.Ordinal);
        Assert.Contains("artifact-cli-list-001", listOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_DiagnosticsQueries_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var traceCommand = ParseCommandWithWorkspace(
            parserType,
            ["diagnostics", "trace", "--trace-id", "trace-cli-001"],
            workingDirectory,
            configPath);
        var attemptsCommand = ParseCommandWithWorkspace(
            parserType,
            ["diagnostics", "attempts", "--execution-id", "execution-cli-001"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetExecutionTraceHandler = query => new ExecutionTrace(
                query.TraceId,
                new ExecutionId("execution-cli-001"),
                [
                    new AttemptSummary(
                        new ExecutionId("execution-cli-001"),
                        AttemptNumber: 1,
                        Succeeded: true,
                        StartedAt: DateTimeOffset.UtcNow,
                        CompletedAt: DateTimeOffset.UtcNow),
                ],
                [
                    new AuditRecord(
                        new AuditRecordId("audit-cli-provider"),
                        "provider_request",
                        "Provider 请求统计 #1",
                        DateTimeOffset.UtcNow,
                        new MetadataBag(new Dictionary<string, StructuredValue>
                        {
                            ["providerRequestSequence"] = StructuredValue.FromNumber("1"),
                            ["estimatedPayloadTokens"] = StructuredValue.FromNumber("128"),
                            ["serializedPayloadChars"] = StructuredValue.FromNumber("4096"),
                            ["artifactFileName"] = StructuredValue.FromString("provider-request-cli.sanitized.json"),
                            ["artifactRelativePath"] = StructuredValue.FromString("provider-requests/provider-request-cli.sanitized.json"),
                        })),
                ]),
            ListAttemptSummariesHandler = query =>
            [
                new AttemptSummary(
                    query.ExecutionId,
                    AttemptNumber: 2,
                    Succeeded: false,
                    StartedAt: DateTimeOffset.UtcNow,
                    CompletedAt: null),
            ],
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (traceExitCode, traceOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", traceCommand);
        var (attemptsExitCode, attemptsOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", attemptsCommand);

        Assert.Equal(0, traceExitCode);
        Assert.Equal(0, attemptsExitCode);
        Assert.Equal("trace-cli-001", Assert.Single(fakeRuntime.ExecutionTraceRequests).TraceId.Value);
        Assert.Equal("execution-cli-001", Assert.Single(fakeRuntime.AttemptSummaryListRequests).ExecutionId.Value);
        Assert.Contains("执行追踪：trace-cli-001", traceOutput, StringComparison.Ordinal);
        Assert.Contains("trace-cli-001", traceOutput, StringComparison.Ordinal);
        Assert.Contains("payloadTokens=128", traceOutput, StringComparison.Ordinal);
        Assert.Contains("provider-request-cli.sanitized.json", traceOutput, StringComparison.Ordinal);
        Assert.Contains("执行尝试：1 条", attemptsOutput, StringComparison.Ordinal);
        Assert.Contains("failed", attemptsOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_IdentityAndMemoryQueries_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var accountId = new AccountId("account-cli-identity");
        var memorySpaceId = new MemorySpaceId("memory-space-cli-identity");

        var accountCommand = ParseCommandWithWorkspace(
            parserType,
            ["identity", "account", "--account-id", accountId.Value, "--json"],
            workingDirectory,
            configPath);
        var devicesCommand = ParseCommandWithWorkspace(
            parserType,
            ["identity", "devices", "--account-id", accountId.Value, "--json"],
            workingDirectory,
            configPath);
        var spacesCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "spaces", "--scope-kind", "workspace", "--json"],
            workingDirectory,
            configPath);
        var overlayCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "overlay", "--memory-space-id", memorySpaceId.Value, "--space-id", "space-cli-memory", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetAccountProfileHandler = query => new Account(query.AccountId, "CLI Identity", "cli.identity@example.com"),
            ListBoundDevicesHandler = query =>
            [
                new DeviceBinding(
                    new DeviceId("device-cli-identity"),
                    query.AccountId,
                    "CLI Laptop",
                    "windows"),
            ],
            ListMemorySpacesHandler = query =>
            [
                new MemorySpace(
                    memorySpaceId,
                    query.ScopeKind ?? MemoryScopeKind.Workspace,
                    "repo://tianshu",
                    "TianShu Workspace",
                    isReadOnly: true),
            ],
            ResolveMemoryOverlayHandler = query => new MemoryOverlay(
            [
                new FactMemoryRecord(
                    "repo_root",
                    StructuredValue.FromPlainObject("D:/Work/TianShu"),
                    query.MemorySpaceId ?? memorySpaceId),
            ],
            new HabitProfile(
                AccountId: accountId,
                PreferredVerbosity: "verbose"),
            MemoryMergeDecision.Applied),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (accountExitCode, accountOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", accountCommand);
        var (devicesExitCode, devicesOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", devicesCommand);
        var (spacesExitCode, spacesOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", spacesCommand);
        var (overlayExitCode, overlayOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", overlayCommand);

        Assert.Equal(0, accountExitCode);
        Assert.Equal(0, devicesExitCode);
        Assert.Equal(0, spacesExitCode);
        Assert.Equal(0, overlayExitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        Assert.Equal(accountId.Value, Assert.Single(fakeRuntime.AccountProfileRequests).AccountId.Value);
        Assert.Equal(accountId.Value, Assert.Single(fakeRuntime.BoundDeviceListRequests).AccountId.Value);
        Assert.Equal(MemoryScopeKind.Workspace, Assert.Single(fakeRuntime.MemorySpaceListRequests).ScopeKind);
        var overlayRequest = Assert.Single(fakeRuntime.MemoryOverlayRequests);
        Assert.Equal(memorySpaceId.Value, overlayRequest.MemorySpaceId?.Value);
        Assert.Equal("space-cli-memory", overlayRequest.CollaborationSpaceId?.Value);

        Assert.Contains("CLI Identity", accountOutput, StringComparison.Ordinal);
        Assert.Contains("device-cli-identity", devicesOutput, StringComparison.Ordinal);
        Assert.Contains("TianShu Workspace", spacesOutput, StringComparison.Ordinal);
        Assert.Contains("repo_root", overlayOutput, StringComparison.Ordinal);
        Assert.Contains("verbose", overlayOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_MemoryCommands_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var memorySpaceId = new MemorySpaceId("memory-space-cli-mutation");
        var memoryRecordId = new MemoryRecordId("memory-record-cli-mutation");

        var providersCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "providers", "--scope-kind", "workspace", "--json"],
            workingDirectory,
            configPath);
        var addCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "add", "--payload-json", "{\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"},\"key\":\"pref.shell\",\"value\":\"pwsh\",\"confidence\":0.9}", "--json"],
            workingDirectory,
            configPath);
        var searchCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "search", "--payload-json", "{\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"},\"key\":\"pref.shell\"}", "--json"],
            workingDirectory,
            configPath);
        var feedbackCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "feedback", "--payload-json", "{\"memoryRecordId\":{\"value\":\"memory-record-cli-mutation\"},\"decision\":\"applied\",\"feedback\":\"accepted\"}", "--json"],
            workingDirectory,
            configPath);
        var deleteCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "delete", "--payload-json", "{\"memoryRecordId\":{\"value\":\"memory-record-cli-mutation\"},\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"},\"key\":\"pref.shell\",\"reason\":\"cli cleanup\"}", "--json"],
            workingDirectory,
            configPath);
        var supersedeCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "supersede", "--payload-json", "{\"oldRecordId\":{\"value\":\"memory-record-cli-mutation\"},\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"},\"newKey\":\"pref.shell.corrected\",\"newValue\":\"powershell\",\"reason\":\"corrected preference\"}", "--json"],
            workingDirectory,
            configPath);
        var reviewListCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "review", "list", "--payload-json", "{\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"}}", "--json"],
            workingDirectory,
            configPath);
        var reviewApproveCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "review", "approve", "--payload-json", "{\"memoryRecordId\":{\"value\":\"memory-record-cli-mutation\"},\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"},\"key\":\"pref.shell\"}", "--json"],
            workingDirectory,
            configPath);
        var reviewDemoteCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "review", "demote", "--payload-json", "{\"memoryRecordId\":{\"value\":\"memory-record-cli-mutation\"},\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"},\"key\":\"pref.shell\",\"reason\":\"low confidence\"}", "--json"],
            workingDirectory,
            configPath);
        var reviewMergeCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "review", "merge", "--payload-json", "{\"reviewRecordId\":{\"value\":\"memory-record-cli-mutation\"},\"targetRecordId\":{\"value\":\"memory-record-cli-active\"},\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"},\"reason\":\"merge duplicate\"}", "--json"],
            workingDirectory,
            configPath);
        var reviewRestoreCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "review", "restore", "--payload-json", "{\"memoryRecordId\":{\"value\":\"memory-record-cli-mutation\"},\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"},\"key\":\"pref.shell\",\"reason\":\"review again\"}", "--json"],
            workingDirectory,
            configPath);
        var consolidationCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "consolidate", "--payload-json", "{\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"},\"includeArchiveProposals\":true,\"enableLease\":false}", "--json"],
            workingDirectory,
            configPath);
        var citationCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "citation", "--payload-json", "{\"citation\":{\"entries\":[{\"memoryRecordId\":{\"value\":\"memory-record-cli-mutation\"},\"memorySpaceId\":{\"value\":\"memory-space-cli-mutation\"},\"key\":\"pref.shell\"}]}}", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListMemoryProvidersHandler = query =>
            [
                new MemoryProviderDescriptor(
                    "provider-cli-local",
                    "CLI Local Provider",
                    "1.0",
                    MemoryProviderCapability.Add | MemoryProviderCapability.Filter,
                    [query.ScopeKind ?? MemoryScopeKind.Workspace])
            ],
            AddMemoryHandler = command => new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Active, Effect: MemoryMutationEffect.Upserted),
            FilterMemoryHandler = query => new MemoryQueryResult(
            [
                new FactMemoryRecord(
                    query.Key ?? "pref.shell",
                    StructuredValue.FromString("pwsh"),
                    query.MemorySpaceId ?? memorySpaceId,
                    id: memoryRecordId)
            ]),
            DeleteMemoryHandler = command => new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Deleted, Effect: MemoryMutationEffect.SoftDeleted),
            SupersedeMemoryHandler = command => new MemoryMutationResult(true, command.OldRecordId, MemoryLifecycleStatus.Archived, Effect: MemoryMutationEffect.Superseded),
            ListMemoryReviewsHandler = query => new MemoryReviewQueryResult(
            [
                new MemoryReviewItem(
                    new FactMemoryRecord(
                        "pref.shell",
                        StructuredValue.FromString("pwsh"),
                        query.MemorySpaceId ?? memorySpaceId,
                        id: memoryRecordId,
                        lifecycleStatus: MemoryLifecycleStatus.PendingReview))
            ]),
            ApproveMemoryReviewHandler = command => new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Active, Effect: MemoryMutationEffect.LifecycleChanged),
            DemoteMemoryReviewHandler = command => new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Archived, Effect: MemoryMutationEffect.LifecycleChanged),
            MergeMemoryReviewHandler = command => new MemoryMutationResult(true, command.TargetRecordId, MemoryLifecycleStatus.Active, Effect: MemoryMutationEffect.Superseded),
            RestoreMemoryReviewHandler = command => new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.PendingReview, Effect: MemoryMutationEffect.LifecycleChanged),
            RunMemoryConsolidationHandler = _ => new MemoryConsolidationRunResult(3, 2, LeaseAcquired: false),
            RecordMemoryFeedbackHandler = command => new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Active),
            RecordMemoryCitationHandler = command => new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Active),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (providersExitCode, providersOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", providersCommand);
        var (addExitCode, addOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", addCommand);
        var (searchExitCode, searchOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", searchCommand);
        var (supersedeExitCode, supersedeOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", supersedeCommand);
        var (reviewListExitCode, reviewListOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", reviewListCommand);
        var (reviewApproveExitCode, reviewApproveOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", reviewApproveCommand);
        var (reviewDemoteExitCode, reviewDemoteOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", reviewDemoteCommand);
        var (reviewMergeExitCode, reviewMergeOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", reviewMergeCommand);
        var (reviewRestoreExitCode, reviewRestoreOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", reviewRestoreCommand);
        var (consolidationExitCode, consolidationOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", consolidationCommand);
        var (feedbackExitCode, feedbackOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", feedbackCommand);
        var (deleteExitCode, deleteOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", deleteCommand);
        var (citationExitCode, citationOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", citationCommand);

        Assert.Equal(0, providersExitCode);
        Assert.Equal(0, addExitCode);
        Assert.Equal(0, searchExitCode);
        Assert.Equal(0, supersedeExitCode);
        Assert.Equal(0, reviewListExitCode);
        Assert.Equal(0, reviewApproveExitCode);
        Assert.Equal(0, reviewDemoteExitCode);
        Assert.Equal(0, reviewMergeExitCode);
        Assert.Equal(0, reviewRestoreExitCode);
        Assert.Equal(0, consolidationExitCode);
        Assert.Equal(0, feedbackExitCode);
        Assert.Equal(0, deleteExitCode);
        Assert.Equal(0, citationExitCode);
        Assert.Equal(MemoryScopeKind.Workspace, Assert.Single(fakeRuntime.MemoryProviderListRequests).ScopeKind);
        Assert.Equal("pref.shell", Assert.Single(fakeRuntime.MemoryAddRequests).Key);
        Assert.Equal(memorySpaceId.Value, Assert.Single(fakeRuntime.MemoryAddRequests).MemorySpaceId.Value);
        var filterRequest = Assert.Single(fakeRuntime.MemoryFilterRequests);
        Assert.Equal("pref.shell", filterRequest.Key);
        Assert.Equal(memorySpaceId.Value, filterRequest.MemorySpaceId?.Value);
        var deleteRequest = Assert.Single(fakeRuntime.MemoryDeleteRequests);
        Assert.Equal(memoryRecordId, deleteRequest.MemoryRecordId);
        Assert.Equal("cli cleanup", deleteRequest.Reason);
        Assert.Equal(memoryRecordId, Assert.Single(fakeRuntime.MemorySupersedeRequests).OldRecordId);
        Assert.Equal(memorySpaceId, Assert.Single(fakeRuntime.MemoryReviewListRequests).MemorySpaceId);
        Assert.Equal(memoryRecordId, Assert.Single(fakeRuntime.MemoryApproveReviewRequests).MemoryRecordId);
        Assert.Equal(memoryRecordId, Assert.Single(fakeRuntime.MemoryDemoteReviewRequests).MemoryRecordId);
        Assert.Equal(new MemoryRecordId("memory-record-cli-active"), Assert.Single(fakeRuntime.MemoryMergeReviewRequests).TargetRecordId);
        Assert.Equal(memoryRecordId, Assert.Single(fakeRuntime.MemoryRestoreReviewRequests).MemoryRecordId);
        var consolidationRequest = Assert.Single(fakeRuntime.MemoryConsolidationRequests);
        Assert.Equal(memorySpaceId, consolidationRequest.MemorySpaceId);
        Assert.True(consolidationRequest.IncludeArchiveProposals);
        Assert.False(consolidationRequest.EnableLease);
        Assert.Equal(memoryRecordId, Assert.Single(fakeRuntime.MemoryFeedbackRequests).MemoryRecordId);
        var citationRequest = Assert.Single(fakeRuntime.MemoryCitationRequests);
        Assert.Equal(memoryRecordId, Assert.Single(citationRequest.Citation.Entries).MemoryRecordId);
        Assert.Contains("provider-cli-local", providersOutput, StringComparison.Ordinal);
        Assert.Contains(memoryRecordId.Value, addOutput, StringComparison.Ordinal);
        Assert.Contains(memoryRecordId.Value, searchOutput, StringComparison.Ordinal);
        Assert.Contains(memoryRecordId.Value, supersedeOutput, StringComparison.Ordinal);
        Assert.Contains(memoryRecordId.Value, reviewListOutput, StringComparison.Ordinal);
        Assert.Contains(memoryRecordId.Value, reviewApproveOutput, StringComparison.Ordinal);
        Assert.Contains(memoryRecordId.Value, reviewDemoteOutput, StringComparison.Ordinal);
        Assert.Contains("memory-record-cli-active", reviewMergeOutput, StringComparison.Ordinal);
        Assert.Contains(memoryRecordId.Value, reviewRestoreOutput, StringComparison.Ordinal);
        Assert.Contains("\"proposalsCreated\": 2", consolidationOutput, StringComparison.Ordinal);
        Assert.Contains(memoryRecordId.Value, feedbackOutput, StringComparison.Ordinal);
        Assert.Contains(memoryRecordId.Value, deleteOutput, StringComparison.Ordinal);
        Assert.Contains(memoryRecordId.Value, citationOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_MemoryCommands_WithoutJson_PrintReadableMemorySummaries()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var memorySpaceId = new MemorySpaceId("memory:workspace:cli-readable");
        var memoryRecordId = new MemoryRecordId("memory-record-cli-readable");

        var addCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "add", "--payload-json", "{\"memorySpaceId\":{\"value\":\"memory:workspace:cli-readable\"},\"key\":\"pref.shell\",\"value\":\"pwsh\",\"confidence\":0.87,\"source\":{\"sourceKind\":\"conversation\",\"sourceId\":\"turn-readable\",\"snippet\":\"用户偏好 PowerShell\"}}"],
            workingDirectory,
            configPath);
        var searchCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "search", "--payload-json", "{\"memorySpaceId\":{\"value\":\"memory:workspace:cli-readable\"},\"key\":\"pref.shell\"}"],
            workingDirectory,
            configPath);
        var deleteCommand = ParseCommandWithWorkspace(
            parserType,
            ["memory", "delete", "--payload-json", "{\"memoryRecordId\":{\"value\":\"memory-record-cli-readable\"},\"memorySpaceId\":{\"value\":\"memory:workspace:cli-readable\"},\"key\":\"pref.shell\",\"reason\":\"cli cleanup\"}"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            AddMemoryHandler = command => new MemoryMutationResult(true, memoryRecordId, MemoryLifecycleStatus.Active, Effect: MemoryMutationEffect.Upserted),
            FilterMemoryHandler = query => new MemoryQueryResult(
            [
                new FactMemoryRecord(
                    query.Key ?? "pref.shell",
                    StructuredValue.FromString("pwsh"),
                    query.MemorySpaceId ?? memorySpaceId,
                    0.87m,
                    id: memoryRecordId,
                    sources:
                    [
                        new MemorySourceRef(
                            MemorySourceKind.Conversation,
                            "turn-readable",
                            snippet: "用户偏好 PowerShell")
                    ])
            ]),
            DeleteMemoryHandler = command => new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Deleted, Effect: MemoryMutationEffect.SoftDeleted),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (addExitCode, addOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", addCommand);
        var (searchExitCode, searchOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", searchCommand);
        var (deleteExitCode, deleteOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", deleteCommand);

        Assert.Equal(0, addExitCode);
        Assert.Equal(0, searchExitCode);
        Assert.Equal(0, deleteExitCode);
        Assert.Contains("记忆操作成功。", addOutput, StringComparison.Ordinal);
        Assert.Contains("Record：memory-record-cli-readable", addOutput, StringComparison.Ordinal);
        Assert.Contains("Space：memory:workspace:cli-readable", addOutput, StringComparison.Ordinal);
        Assert.Contains("Scope：workspace", addOutput, StringComparison.Ordinal);
        Assert.Contains("Key：pref.shell", addOutput, StringComparison.Ordinal);
        Assert.Contains("Confidence：0.87", addOutput, StringComparison.Ordinal);
        Assert.Contains("Source：Conversation:turn-readable", addOutput, StringComparison.Ordinal);
        Assert.Contains("记忆查询：1 / 1 条", searchOutput, StringComparison.Ordinal);
        Assert.Contains("space=memory:workspace:cli-readable", searchOutput, StringComparison.Ordinal);
        Assert.Contains("scope=workspace", searchOutput, StringComparison.Ordinal);
        Assert.Contains("key=pref.shell", searchOutput, StringComparison.Ordinal);
        Assert.Contains("confidence=0.87", searchOutput, StringComparison.Ordinal);
        Assert.Contains("source=Conversation:turn-readable", searchOutput, StringComparison.Ordinal);
        Assert.Contains("效果：SoftDeleted", deleteOutput, StringComparison.Ordinal);
        Assert.Contains("Space：memory:workspace:cli-readable", deleteOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_CollaborationQueries_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var collaborationSpaceId = new CollaborationSpaceId("space-collab-cli");

        var overviewCommand = ParseCommandWithWorkspace(
            parserType,
            ["collaboration", "overview", "--space-id", collaborationSpaceId.Value, "--json"],
            workingDirectory,
            configPath);
        var readCommand = ParseCommandWithWorkspace(
            parserType,
            ["collaboration", "read", "--space-id", collaborationSpaceId.Value, "--json"],
            workingDirectory,
            configPath);
        var listCommand = ParseCommandWithWorkspace(
            parserType,
            ["collaboration", "list", "--include-archived", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetCollaborationSpaceOverviewHandler = query => new CollaborationSpaceOverviewProjection(query.SpaceId, "cli-space", "CLI Space", false),
            GetCollaborationSpaceProjectionHandler = query => new TianShu.Contracts.Projections.CollaborationSpaceProjection(
                new CollaborationSpaceRef(query.SpaceId, "cli-space", "CLI Space"),
                ActiveSessionCount: 2,
                ActiveThreadCount: 3,
                IsArchived: false),
            ListCollaborationSpacesHandler = query =>
            [
                new CollaborationSpaceOverviewProjection(
                    new CollaborationSpaceId("space-collab-cli-list"),
                    "cli-space-list",
                    "CLI Space List",
                    query.IncludeArchived),
            ],
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (overviewExitCode, overviewOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", overviewCommand);
        var (readExitCode, readOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", readCommand);
        var (listExitCode, listOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", listCommand);

        Assert.Equal(0, overviewExitCode);
        Assert.Equal(0, readExitCode);
        Assert.Equal(0, listExitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        Assert.Equal(collaborationSpaceId.Value, Assert.Single(fakeRuntime.CollaborationSpaceOverviewRequests).SpaceId.Value);
        Assert.Equal(collaborationSpaceId.Value, Assert.Single(fakeRuntime.CollaborationSpaceProjectionRequests).SpaceId.Value);
        Assert.True(Assert.Single(fakeRuntime.CollaborationSpaceListRequests).IncludeArchived);

        Assert.Contains("CLI Space", overviewOutput, StringComparison.Ordinal);
        Assert.Contains("\"activeSessionCount\": 2", readOutput, StringComparison.Ordinal);
        Assert.Contains("space-collab-cli-list", listOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_CollaborationCommands_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var createCommand = ParseCommandWithWorkspace(
            parserType,
            ["collaboration", "create", "--space-id", "space-collab-create", "--key", "team-alpha", "--display-name", "Team Alpha", "--purpose", "Cross repo collaboration", "--default-workspace", workingDirectory, "--default-execution-profile", "review", "--policy-key", "policy-alpha", "--json"],
            workingDirectory,
            configPath);
        var configureCommand = ParseCommandWithWorkspace(
            parserType,
            ["collaboration", "configure", "--space-id", "space-collab-create", "--display-name", "Team Alpha v2", "--purpose", "Updated purpose", "--json"],
            workingDirectory,
            configPath);
        var archiveCommand = ParseCommandWithWorkspace(
            parserType,
            ["collaboration", "archive", "--space-id", "space-collab-create", "--json"],
            workingDirectory,
            configPath);
        var bindSessionCommand = ParseCommandWithWorkspace(
            parserType,
            ["participant", "bind-session", "--participant-id", "participant-bind-001", "--session-id", "session-bind-001", "--json"],
            workingDirectory,
            configPath);
        var bindWorkflowCommand = ParseCommandWithWorkspace(
            parserType,
            ["participant", "bind-workflow", "--participant-id", "participant-bind-001", "--workflow-id", "workflow-bind-001", "--json"],
            workingDirectory,
            configPath);
        var updateRoleCommand = ParseCommandWithWorkspace(
            parserType,
            ["participant", "update-role", "--participant-id", "participant-bind-001", "--role", "owner", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            CreateCollaborationSpaceHandler = command => new CollaborationSpace(
                command.SpaceId,
                command.Key,
                command.DisplayName,
                command.Profile,
                command.Defaults,
                command.PolicyRef),
            ConfigureCollaborationSpaceHandler = command => new CollaborationSpace(
                command.SpaceId,
                "team-alpha",
                command.DisplayName ?? "Team Alpha",
                command.Profile ?? new CollaborationSpaceProfile("Updated purpose"),
                command.Defaults ?? CollaborationDefaultSet.Empty,
                command.PolicyRef),
            ArchiveCollaborationSpaceHandler = _ => true,
            BindParticipantToSessionHandler = _ => true,
            BindParticipantToWorkflowHandler = _ => true,
            UpdateParticipantRoleHandler = _ => true,
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (createExitCode, createOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", createCommand);
        var (configureExitCode, configureOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", configureCommand);
        var (archiveExitCode, archiveOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", archiveCommand);
        var (bindSessionExitCode, bindSessionOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", bindSessionCommand);
        var (bindWorkflowExitCode, bindWorkflowOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", bindWorkflowCommand);
        var (updateRoleExitCode, updateRoleOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", updateRoleCommand);

        Assert.Equal(0, createExitCode);
        Assert.Equal(0, configureExitCode);
        Assert.Equal(0, archiveExitCode);
        Assert.Equal(0, bindSessionExitCode);
        Assert.Equal(0, bindWorkflowExitCode);
        Assert.Equal(0, updateRoleExitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var createRequest = Assert.Single(fakeRuntime.CollaborationSpaceCreateRequests);
        Assert.Equal("space-collab-create", createRequest.SpaceId.Value);
        Assert.Equal("team-alpha", createRequest.Key);
        Assert.Equal("Cross repo collaboration", createRequest.Profile.Purpose);
        Assert.Equal(workingDirectory, createRequest.Defaults.DefaultWorkspace);
        Assert.Equal("review", createRequest.Defaults.DefaultExecutionProfile);
        Assert.Equal("policy-alpha", createRequest.PolicyRef?.PolicyKey);

        var configureRequest = Assert.Single(fakeRuntime.CollaborationSpaceConfigureRequests);
        Assert.Equal("space-collab-create", configureRequest.SpaceId.Value);
        Assert.Equal("Team Alpha v2", configureRequest.DisplayName);
        Assert.Equal("Updated purpose", configureRequest.Profile?.Purpose);

        Assert.Equal("space-collab-create", Assert.Single(fakeRuntime.CollaborationSpaceArchiveRequests).SpaceId.Value);
        Assert.Equal("session-bind-001", Assert.Single(fakeRuntime.ParticipantSessionBindRequests).SessionId.Value);
        Assert.Equal("workflow-bind-001", Assert.Single(fakeRuntime.ParticipantWorkflowBindRequests).WorkflowId.Value);
        Assert.Equal("owner", Assert.Single(fakeRuntime.ParticipantRoleUpdateRequests).Role);

        Assert.Contains("\"key\": \"team-alpha\"", createOutput, StringComparison.Ordinal);
        Assert.Contains("\"displayName\": \"Team Alpha v2\"", configureOutput, StringComparison.Ordinal);
        Assert.Contains("true", archiveOutput, StringComparison.Ordinal);
        Assert.Contains("true", bindSessionOutput, StringComparison.Ordinal);
        Assert.Contains("true", bindWorkflowOutput, StringComparison.Ordinal);
        Assert.Contains("true", updateRoleOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ParticipantQueries_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var participantId = new ParticipantId("participant-cli-read");
        var collaborationSpaceId = new CollaborationSpaceId("space-participant-cli");

        var readCommand = ParseCommandWithWorkspace(
            parserType,
            ["participant", "read", "--participant-id", participantId.Value, "--json"],
            workingDirectory,
            configPath);
        var viewCommand = ParseCommandWithWorkspace(
            parserType,
            ["participant", "view", "--participant-id", participantId.Value, "--json"],
            workingDirectory,
            configPath);
        var listCommand = ParseCommandWithWorkspace(
            parserType,
            ["participant", "list", "--space-id", collaborationSpaceId.Value, "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetParticipantProjectionHandler = query => new ParticipantProjection(query.ParticipantId, ParticipantKind.Agent, "Hermes", "reviewer"),
            GetParticipantViewProjectionHandler = query => new TianShu.Contracts.Projections.ParticipantProjection(
                new ParticipantRef(query.ParticipantId, ParticipantKind.Agent, "Hermes"),
                ScopeKind: "participant",
                ScopeKey: query.ParticipantId.Value,
                Role: "reviewer",
                IsActive: true),
            ListParticipantsInScopeHandler = query =>
            [
                new ParticipantProjection(new ParticipantId("participant-cli-list"), ParticipantKind.Agent, query.CollaborationSpaceId.Value, "owner"),
            ],
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (readExitCode, readOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", readCommand);
        var (viewExitCode, viewOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", viewCommand);
        var (listExitCode, listOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", listCommand);

        Assert.Equal(0, readExitCode);
        Assert.Equal(0, viewExitCode);
        Assert.Equal(0, listExitCode);

        Assert.Equal(participantId.Value, Assert.Single(fakeRuntime.ParticipantProjectionRequests).ParticipantId.Value);
        Assert.Equal(participantId.Value, Assert.Single(fakeRuntime.ParticipantViewProjectionRequests).ParticipantId.Value);
        Assert.Equal(collaborationSpaceId.Value, Assert.Single(fakeRuntime.ParticipantListRequests).CollaborationSpaceId.Value);

        Assert.Contains("Hermes", readOutput, StringComparison.Ordinal);
        Assert.Contains("\"participant\"", viewOutput, StringComparison.Ordinal);
        Assert.Contains("participant-cli-list", listOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_WorkflowQueries_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var collaborationSpace = new CollaborationSpaceRef(new CollaborationSpaceId("space-workflow-cli"), "space-workflow-cli", "Workflow CLI");

        var boardCommand = ParseCommandWithWorkspace(
            parserType,
            ["workflow", "board", "--workflow-id", "workflow-cli-001", "--json"],
            workingDirectory,
            configPath);
        var taskBoardCommand = ParseCommandWithWorkspace(
            parserType,
            ["workflow", "taskboard", "--workflow-id", "workflow-cli-001", "--json"],
            workingDirectory,
            configPath);
        var planCommand = ParseCommandWithWorkspace(
            parserType,
            ["workflow", "plan", "--workflow-id", "workflow-cli-001", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetWorkflowBoardProjectionHandler = query => new WorkflowBoardProjection(query.WorkflowId, "CLI Workflow Board", collaborationSpace, 1, 2, 3),
            GetTaskBoardProjectionHandler = query => new TaskBoardProjection(
                query.WorkflowId,
                [new TaskBoardItem(new TaskId("task-cli-001"), "Wire CLI workflow query", "running")]),
            GetPlanProjectionHandler = query => new PlanProjection(
                query.WorkflowId,
                new Plan("CLI Workflow Plan", [new PlanStep(0, "Mirror workflow formal query")]))
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (boardExitCode, boardOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", boardCommand);
        var (taskBoardExitCode, taskBoardOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", taskBoardCommand);
        var (planExitCode, planOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", planCommand);

        Assert.Equal(0, boardExitCode);
        Assert.Equal(0, taskBoardExitCode);
        Assert.Equal(0, planExitCode);

        Assert.Equal("workflow-cli-001", Assert.Single(fakeRuntime.WorkflowBoardRequests).WorkflowId.Value);
        Assert.Equal("workflow-cli-001", Assert.Single(fakeRuntime.TaskBoardRequests).WorkflowId.Value);
        Assert.Equal("workflow-cli-001", Assert.Single(fakeRuntime.PlanProjectionRequests).WorkflowId.Value);

        Assert.Contains("CLI Workflow Board", boardOutput, StringComparison.Ordinal);
        Assert.Contains("task-cli-001", taskBoardOutput, StringComparison.Ordinal);
        Assert.Contains("CLI Workflow Plan", planOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_WorkflowWriteCommands_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var createCommand = ParseCommandWithWorkspace(
            parserType,
            ["workflow", "create", "--workflow-id", "workflow-cli-write-001", "--space-id", "space-cli-write-001", "--display-name", "CLI Workflow Write", "--thread-id", "thread-cli-write-001", "--participant-id", "participant-cli-write-001", "--json"],
            workingDirectory,
            configPath);
        var publishCommand = ParseCommandWithWorkspace(
            parserType,
            ["workflow", "publish-plan", "--workflow-id", "workflow-cli-write-001", "--title", "CLI Write Plan", "--steps-json", """[{"title":"Mirror workflow write commands","description":"cli formal write"}]""", "--json"],
            workingDirectory,
            configPath);
        var createTaskCommand = ParseCommandWithWorkspace(
            parserType,
            ["workflow", "create-task", "--task-id", "task-cli-write-001", "--workflow-id", "workflow-cli-write-001", "--title", "Implement CLI workflow writes", "--state", "in-progress", "--participant-id", "participant-cli-write-001", "--json"],
            workingDirectory,
            configPath);
        var updateTaskCommand = ParseCommandWithWorkspace(
            parserType,
            ["workflow", "update-task-state", "--task-id", "task-cli-write-001", "--state", "done", "--participant-id", "participant-cli-write-001", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            CreateWorkflowHandler = request => new Workflow(
                request.WorkflowId,
                new CollaborationSpaceRef(request.CollaborationSpaceId, request.CollaborationSpaceId.Value, "CLI Write Space"),
                request.DisplayName,
                WorkflowState.Draft,
                request.OwnerParticipant,
                request.ThreadId),
            PublishPlanHandler = request => new PlanProjection(request.WorkflowId, request.Plan),
            CreateWorkflowTaskHandler = request => request.Task,
            UpdateWorkflowTaskStateHandler = request => new TianShu.Contracts.Workflows.Task(
                request.TaskId,
                new WorkflowId("workflow-cli-write-001"),
                "Implement CLI workflow writes",
                request.State,
                request.OwnerParticipant),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (createExitCode, createOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", createCommand);
        var (publishExitCode, publishOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", publishCommand);
        var (createTaskExitCode, createTaskOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", createTaskCommand);
        var (updateTaskExitCode, updateTaskOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", updateTaskCommand);

        Assert.Equal(0, createExitCode);
        Assert.Equal(0, publishExitCode);
        Assert.Equal(0, createTaskExitCode);
        Assert.Equal(0, updateTaskExitCode);

        var createRequest = Assert.Single(fakeRuntime.WorkflowCreateRequests);
        Assert.Equal("workflow-cli-write-001", createRequest.WorkflowId.Value);
        Assert.Equal("space-cli-write-001", createRequest.CollaborationSpaceId.Value);
        Assert.Equal("thread-cli-write-001", createRequest.ThreadId?.Value);
        Assert.Equal("participant-cli-write-001", createRequest.OwnerParticipant?.Id.Value);

        var publishRequest = Assert.Single(fakeRuntime.WorkflowPublishPlanRequests);
        Assert.Equal("CLI Write Plan", publishRequest.Plan.Title);
        Assert.Equal("Mirror workflow write commands", Assert.Single(publishRequest.Plan.Steps).Title);

        var createTaskRequest = Assert.Single(fakeRuntime.WorkflowCreateTaskRequests);
        Assert.Equal("task-cli-write-001", createTaskRequest.Task.Id.Value);
        Assert.Equal(TaskState.InProgress, createTaskRequest.Task.State);

        var updateTaskRequest = Assert.Single(fakeRuntime.WorkflowUpdateTaskStateRequests);
        Assert.Equal("task-cli-write-001", updateTaskRequest.TaskId.Value);
        Assert.Equal(TaskState.Done, updateTaskRequest.State);

        Assert.Contains("CLI Workflow Write", createOutput, StringComparison.Ordinal);
        Assert.Contains("CLI Write Plan", publishOutput, StringComparison.Ordinal);
        Assert.Contains("Implement CLI workflow writes", createTaskOutput, StringComparison.Ordinal);
        Assert.Contains("Implement CLI workflow writes", updateTaskOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_AgentQueries_WithInjectedRuntime_UseFormalRequests()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var participant = new ParticipantRef(new ParticipantId("participant-cli-001"), ParticipantKind.Agent, "Hermes");

        var rosterCommand = ParseCommandWithWorkspace(
            parserType,
            ["agent", "roster", "--workflow-id", "workflow-agent-cli", "--json"],
            workingDirectory,
            configPath);
        var teamCommand = ParseCommandWithWorkspace(
            parserType,
            ["agent", "team", "--team-id", "team-cli-001", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            GetAgentRosterProjectionHandler = _ => new AgentRosterProjection(
            [
                new AgentRosterEntry(
                    new AgentId("agent-cli-001"),
                    participant,
                    "reviewer",
                    2,
                    true),
            ]),
            GetTeamProjectionHandler = query => new TeamProjection(
                new Team(query.TeamId, "CLI Team", [new AgentId("agent-cli-001")]),
                [
                    new Agent(
                        new AgentId("agent-cli-001"),
                        participant,
                        "Hermes",
                        AgentRole.Reviewer),
                ])
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (rosterExitCode, rosterOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", rosterCommand);
        var (teamExitCode, teamOutput) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", teamCommand);

        Assert.Equal(0, rosterExitCode);
        Assert.Equal(0, teamExitCode);

        Assert.Equal("workflow-agent-cli", Assert.Single(fakeRuntime.AgentRosterProjectionRequests).WorkflowId?.Value);
        Assert.Equal("team-cli-001", Assert.Single(fakeRuntime.TeamProjectionRequests).TeamId.Value);

        Assert.Contains("Hermes", rosterOutput, StringComparison.Ordinal);
        Assert.Contains("team-cli-001", teamOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ConfigValueWrite_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var filePath = Path.Combine(workingDirectory, "config.override.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["config", "write", "--key", "shell_environment_policy.inherit", "--value-json", "false", "--file-path", filePath, "--expected-version", "v1"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            WriteConfigValueHandler = _ => new ControlPlaneConfigWriteResult
            {
                Status = "ok",
                Version = "v2",
                FilePath = filePath,
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.ConfigValueWriteRequests);
        Assert.Equal("shell_environment_policy.inherit", request.KeyPath);
        Assert.NotNull(request.Value);
        Assert.Equal(StructuredValueKind.Boolean, request.Value!.Kind);
        Assert.False(request.Value.BooleanValue);
        Assert.Equal(workingDirectory, request.WorkingDirectory);
        Assert.Equal(filePath, request.FilePath);
        Assert.Equal("v1", request.ExpectedVersion);
        Assert.Contains("status=ok", output, StringComparison.Ordinal);
        Assert.Contains("version=v2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_ConfigBatchWrite_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var filePath = Path.Combine(workingDirectory, "config.override.toml");
        var itemsPath = Path.Combine(workingDirectory, "config-items.json");
        File.WriteAllText(itemsPath, """[{"keyPath":"profiles.default.model","value":"gpt-5"},{"keyPath":"shell_environment_policy.inherit","value":false}]""");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["config", "batch-write", "--items-file", itemsPath, "--file-path", filePath, "--expected-version", "v2", "--reload-user-config"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            WriteConfigBatchHandler = _ => new ControlPlaneConfigWriteResult
            {
                Status = "okOverridden",
                Version = "v3",
                FilePath = filePath,
                IsOverridden = true,
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.ConfigBatchWriteRequests);
        Assert.Equal(workingDirectory, request.WorkingDirectory);
        Assert.Equal(filePath, request.FilePath);
        Assert.Equal("v2", request.ExpectedVersion);
        Assert.True(request.ReloadUserConfig);
        Assert.Collection(
            request.Items,
            item =>
            {
                Assert.Equal("profiles.default.model", item.KeyPath);
                Assert.NotNull(item.Value);
                Assert.Equal(StructuredValueKind.String, item.Value!.Kind);
                Assert.Equal("gpt-5", item.Value.StringValue);
            },
            item =>
            {
                Assert.Equal("shell_environment_policy.inherit", item.KeyPath);
                Assert.NotNull(item.Value);
                Assert.Equal(StructuredValueKind.Boolean, item.Value!.Kind);
                Assert.False(item.Value.BooleanValue);
            });
        Assert.Contains("status=okOverridden", output, StringComparison.Ordinal);
        Assert.Contains("version=v3", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_SkillsList_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var extraRoot = Path.Combine(workingDirectory, ".tianshu-extra-skills");
        var skillDirectory = Path.Combine(workingDirectory, ".tianshu", "skills", "demo-search");
        var skillPath = Path.Combine(skillDirectory, "SKILL.md");
        Directory.CreateDirectory(extraRoot);
        Directory.CreateDirectory(skillDirectory);

        var command = ParseCommandWithWorkspace(
            parserType,
            ["skills", "list", "--force-reload", "--extra-root", extraRoot],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListSkillsHandler = _ => new ControlPlaneSkillCatalogResult
            {
                Entries =
                [
                    new ControlPlaneSkillCatalogEntry
                    {
                        WorkingDirectory = workingDirectory,
                        Skills =
                        [
                            new ControlPlaneSkillDescriptor
                            {
                                Scope = "repo",
                                Name = "demo-search",
                                Path = skillPath,
                            },
                        ],
                        Errors =
                        [
                            new ControlPlaneSkillError
                            {
                                Path = Path.Combine(extraRoot, "broken"),
                                Message = "manifest 缺少 name",
                            },
                        ],
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.SkillsListRequests);
        Assert.Equal(workingDirectory, Assert.Single(request.WorkingDirectories));
        Assert.True(request.ForceReload);
        var extraRoots = Assert.Single(request.ExtraRootsByWorkingDirectory);
        Assert.Equal(workingDirectory, extraRoots.WorkingDirectory);
        Assert.Equal(extraRoot, Assert.Single(extraRoots.ExtraUserRoots));

        Assert.Contains($"[{workingDirectory}]", output, StringComparison.Ordinal);
        Assert.Contains($"repo\tdemo-search\t{skillPath}", output, StringComparison.Ordinal);
        Assert.Contains($"! {Path.Combine(extraRoot, "broken")}: manifest 缺少 name", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_SkillsConfigWrite_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");
        var skillPath = Path.Combine(workingDirectory, ".tianshu", "skills", "demo-enable");
        Directory.CreateDirectory(skillPath);

        var command = ParseCommandWithWorkspace(
            parserType,
            ["skills", "enable", "--path", skillPath],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            WriteSkillsConfigHandler = _ => new ControlPlaneSkillConfigWriteResult
            {
                EffectiveEnabled = true,
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.SkillsConfigWriteRequests);
        Assert.Equal(skillPath, request.Path);
        Assert.True(request.Enabled);
        Assert.Equal(workingDirectory, request.WorkingDirectory);

        Assert.Contains($"已启用技能：{skillPath}", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_SkillsRemoteList_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["skills", "remote-list", "--hazelnut-scope", "org"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListRemoteSkillsHandler = _ => new ControlPlaneRemoteSkillCatalogResult
            {
                Items =
                [
                    new ControlPlaneRemoteSkillSummary
                    {
                        Id = "skill_remote_001",
                        Name = "Remote Search",
                        HazelnutScope = "org",
                    },
                ],
                NextCursor = "cursor_remote_001",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.SkillsRemoteListRequests);
        Assert.Equal("org", request.HazelnutScope);
        Assert.Null(request.ProductSurface);
        Assert.Null(request.Enabled);
        Assert.Contains("skill_remote_001\tRemote Search\torg", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_SkillsRemoteExport_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["skills", "remote-export", "--hazelnut-id", "skill_remote_001"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ExportRemoteSkillHandler = _ => new ControlPlaneRemoteSkillExportResult
            {
                Id = "skill_remote_001",
                Path = "D:/Exports/skill_remote_001",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.SkillsRemoteExportRequests);
        Assert.Equal("skill_remote_001", request.HazelnutId);
        Assert.Contains("远程技能已导出：D:/Exports/skill_remote_001", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_PluginList_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["plugin", "list"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListPluginsHandler = _ => new ControlPlanePluginCatalogResult
            {
                Marketplaces =
                [
                    new ControlPlanePluginMarketplace
                    {
                        Name = "debug",
                        Path = "D:/marketplace/marketplace.json",
                        Plugins =
                        [
                            new ControlPlanePluginSummary
                            {
                                Name = "demo-plugin",
                                Enabled = true,
                                Source = StructuredValue.FromPlainObject(new Dictionary<string, object?>
                                {
                                    ["type"] = "local",
                                    ["path"] = "D:/marketplace/demo-plugin",
                                }),
                            },
                        ],
                    },
                ],
                RemoteSyncError = "sync timeout",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.PluginListRequests);
        Assert.Equal(workingDirectory, Assert.Single(request.WorkingDirectories));

        Assert.Contains("[debug] D:/marketplace/marketplace.json", output, StringComparison.Ordinal);
        Assert.Contains("enabled\tdemo-plugin\tD:/marketplace/demo-plugin", output, StringComparison.Ordinal);
        Assert.Contains("remoteSyncError=sync timeout", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_PluginListForceRemoteSync_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["plugin", "list", "--force-remote-sync"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ListPluginsHandler = _ => new ControlPlanePluginCatalogResult
            {
                RemoteSyncError = "forceRemoteSync not implemented",
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        var request = Assert.Single(fakeRuntime.PluginListRequests);
        Assert.True(request.ForceRemoteSync);
        Assert.Equal(workingDirectory, Assert.Single(request.WorkingDirectories));
        Assert.Contains("remoteSyncError=forceRemoteSync not implemented", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_PluginUninstall_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["plugin", "uninstall", "--plugin-id", "demo-plugin@debug"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            UninstallPluginHandler = _ => new ControlPlanePluginUninstallResult(),
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var request = Assert.Single(fakeRuntime.PluginUninstallRequests);
        Assert.Equal("demo-plugin@debug", request.PluginId);
        Assert.Equal(workingDirectory, request.WorkingDirectory);
        Assert.Contains("插件已卸载：demo-plugin@debug", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunMcpAsync_McpRemove_WhenServerMissing_PrintsNotFound()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["mcp", "remove", "demo"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadConfigHandler = _ => new ControlPlaneConfigSnapshotResult
            {
                Config = StructuredValueTestHelper.FromJson(
                    """
                    {
                      "mcp_servers": {
                        "other": {
                          "command": "node"
                        }
                      }
                    }
                    """),
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunMcpAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var request = Assert.Single(fakeRuntime.ConfigReadRequests);
        Assert.Null(request.WorkingDirectory);
        Assert.Empty(fakeRuntime.ConfigBatchWriteRequests);
        Assert.Empty(fakeRuntime.Invocations);
        Assert.Contains("No MCP server named 'demo' found.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_AgentThreadRegister_WithInjectedRuntime_UsesFormalRequest()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["agent", "thread", "register", "--thread-id", "thread_agent_001", "--agent-nickname", "helper", "--agent-role", "reviewer"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            RegisterAgentThreadHandler = request => new ControlPlaneAgentThreadRegistrationResult
            {
                Agent = new ControlPlaneAgentDescriptor
                {
                    ThreadId = request.ThreadId,
                    WorkingDirectory = "D:/Work/TianShu",
                    Preview = "agent registered",
                    AgentNickname = request.AgentNickname,
                    AgentRole = request.AgentRole,
                    UpdatedAt = new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.Zero),
                },
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.AgentThreadRegistrationCommands);
        Assert.Equal("thread_agent_001", request.ThreadId.Value);
        Assert.Equal("helper", request.AgentNickname);
        Assert.Equal("reviewer", request.AgentRole);
        Assert.Empty(fakeRuntime.Invocations);
        Assert.Contains("已登记线程 Agent 元数据。", output, StringComparison.Ordinal);
        Assert.Contains("线程：thread_agent_001", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_AgentThreadRegister_WithInjectedRuntime_OutputJson_PreservesLegacyShapeLocally()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["agent", "thread", "register", "--thread-id", "thread_agent_002", "--agent-nickname", "builder", "--agent-role", "implementer", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            RegisterAgentThreadHandler = request => new ControlPlaneAgentThreadRegistrationResult
            {
                Agent = new ControlPlaneAgentDescriptor
                {
                    ThreadId = request.ThreadId,
                    Preview = "typed registration",
                    WorkingDirectory = "D:/Work/TianShu",
                    AgentNickname = request.AgentNickname,
                    AgentRole = request.AgentRole,
                    UpdatedAt = new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.Zero),
                },
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        var request = Assert.Single(fakeRuntime.AgentThreadRegistrationCommands);
        Assert.Equal("thread_agent_002", request.ThreadId.Value);

        using var document = JsonDocument.Parse(output);
        var thread = document.RootElement.GetProperty("thread");
        Assert.Equal("thread_agent_002", thread.GetProperty("id").GetString());
        Assert.Equal("builder", thread.GetProperty("agentNickname").GetString());
        Assert.Equal("implementer", thread.GetProperty("agentRole").GetString());
        Assert.Equal(0, thread.GetProperty("turns").GetArrayLength());
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_AgentJobCreate_WithInjectedRuntime_UsesFormalRequest()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["agent", "job", "create", "--instruction", "Handle items", "--name", "Batch job"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            CreateAgentJobHandler = _ => new ControlPlaneJobOperationResult
            {
                Job = new ControlPlaneJobDetails
                {
                    Id = new JobId("job_001"),
                    Name = "Batch job",
                    Status = "pending",
                    Instruction = "Handle items",
                },
                Items =
                [
                    new ControlPlaneJobItemDetails { ItemId = new JobItemId("item_001") },
                    new ControlPlaneJobItemDetails { ItemId = new JobItemId("item_002") },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.AgentJobCreateRequests);
        Assert.Equal("Handle items", request.Instruction);
        Assert.Equal("Batch job", request.Name);
        Assert.Empty(fakeRuntime.Invocations);
        Assert.Contains("已创建 Agent Job。", output, StringComparison.Ordinal);
        Assert.Contains("Job：job_001", output, StringComparison.Ordinal);
        Assert.Contains("条目数：2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_AgentJobCreate_WithInjectedRuntime_OutputJson_PreservesLegacyShape()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["agent", "job", "create", "--instruction", "Handle items", "--name", "Batch job", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            CreateAgentJobHandler = _ => new ControlPlaneJobOperationResult
            {
                Job = new ControlPlaneJobDetails
                {
                    Id = new JobId("job_001"),
                    Name = "Batch job",
                    Status = "pending",
                    Instruction = "Handle items",
                },
                Items =
                [
                    new ControlPlaneJobItemDetails
                    {
                        ItemId = new JobItemId("item_001"),
                        Status = "running",
                    },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        var request = Assert.Single(fakeRuntime.AgentJobCreateRequests);
        Assert.Equal("Handle items", request.Instruction);

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal("job_001", root.GetProperty("job").GetProperty("id").GetString());
        Assert.Equal("Batch job", root.GetProperty("job").GetProperty("name").GetString());
        Assert.Equal("item_001", root.GetProperty("items")[0].GetProperty("itemId").GetString());
        Assert.Equal("running", root.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_AgentJobItemReport_WithInjectedRuntime_UsesFormalRequest()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["agent", "job", "report-item", "--job-id", "job_001", "--item-id", "item_001", "--status", "completed", "--result-json", "{\"score\":99}", "--last-error", "none"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReportAgentJobItemHandler = _ => new ControlPlaneJobOperationResult
            {
                Job = new ControlPlaneJobDetails
                {
                    Id = new JobId("job_001"),
                },
                Item = new ControlPlaneJobItemDetails
                {
                    ItemId = new JobItemId("item_001"),
                    Status = "completed",
                    LastError = "none",
                },
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.AgentJobItemReportRequests);
        Assert.Equal("job_001", request.JobId.Value);
        Assert.Equal("item_001", request.ItemId.Value);
        Assert.Equal("completed", request.Status);
        Assert.NotNull(request.Result);
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(request.Result.ToPlainObject())))
        {
            Assert.Equal(99, document.RootElement.GetProperty("score").GetInt32());
        }
        Assert.Equal("none", request.LastError);
        Assert.Empty(fakeRuntime.Invocations);
        Assert.Contains("已上报 Agent Job 条目结果。", output, StringComparison.Ordinal);
        Assert.Contains("Job：job_001", output, StringComparison.Ordinal);
        Assert.Contains("条目：item_001", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRuntimeSurfaceAsync_AgentJobRead_WithInjectedRuntime_UsesFormalRequest()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["agent", "job", "read", "--job-id", "job_001"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ReadAgentJobHandler = _ => new ControlPlaneJobOperationResult
            {
                Job = new ControlPlaneJobDetails
                {
                    Id = new JobId("job_001"),
                    Name = "Batch job",
                    Status = "completed",
                    Instruction = "Handle items",
                },
                Items =
                [
                    new ControlPlaneJobItemDetails { ItemId = new JobItemId("item_001") },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.AgentJobReadRequests);
        Assert.Equal("job_001", request.JobId.Value);
        Assert.Empty(fakeRuntime.Invocations);
        Assert.Contains("已读取 Agent Job。", output, StringComparison.Ordinal);
        Assert.Contains("Job：job_001", output, StringComparison.Ordinal);
        Assert.Contains("条目数：1", output, StringComparison.Ordinal);
    }
    [Fact]
    public async Task RunRuntimeSurfaceAsync_AgentJobDispatch_WithInjectedRuntime_UsesFormalRequest()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliRuntimeCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["agent", "job", "dispatch", "--job-id", "job_001", "--thread-id", "thread_a", "--thread-id", "thread_b"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            DispatchAgentJobHandler = _ => new ControlPlaneJobOperationResult
            {
                Items =
                [
                    new ControlPlaneJobItemDetails { ItemId = new JobItemId("item_001"), ThreadId = new ThreadId("thread_a"), Status = "running" },
                    new ControlPlaneJobItemDetails { ItemId = new JobItemId("item_002"), AssignedThreadId = new ThreadId("thread_b"), Status = "running" },
                ],
            },
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunRuntimeSurfaceAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);

        var request = Assert.Single(fakeRuntime.AgentJobDispatchRequests);
        Assert.Equal("job_001", request.JobId.Value);
        Assert.Equal(new[] { "thread_a", "thread_b" }, request.ThreadIds.Select(static item => item.Value).ToArray());
        Assert.Empty(fakeRuntime.Invocations);
        Assert.Contains("已分发 Agent Job 条目。", output, StringComparison.Ordinal);
        Assert.Contains("item_001\tthread_a\trunning", output, StringComparison.Ordinal);
        Assert.Contains("item_002\tthread_b\trunning", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunFollowUpAsync_WithInjectedRuntime_UsesTypedRuntime()
    {
        var parserType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.CliCommandParser");
        var runnerType = ReflectionTestHelper.GetRequiredType(CliAssembly, "TianShu.Cli.ConversationTurnCommandRunner");
        var workingDirectory = CreateWorkspace(
            createSolution: true,
            createKernelProject: true,
            createConfig: true);
        var configPath = Path.Combine(workingDirectory, ".tianshu", "tianshu.toml");

        var command = ParseCommandWithWorkspace(
            parserType,
            ["follow-up", "--mode", "interrupt", "--message", "继续推进", "--json"],
            workingDirectory,
            configPath);

        var fakeRuntime = new FakeAgentRuntime
        {
            ActiveThreadId = "thread-followup-typed-001",
        };
        fakeRuntime.SendFollowUpInputsAsyncHandler = (userInputs, mode, _, correlationId) =>
        {
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextDelta,
                fakeRuntime.ActiveThreadId,
                "turn-followup-typed-001",
                text: "follow-up typed 路径验证通过"));
            fakeRuntime.EmitStreamEvent(CreateTypedStreamEvent(
                ControlPlaneConversationStreamEventKind.AssistantTextCompleted,
                fakeRuntime.ActiveThreadId,
                "turn-followup-typed-001"));

            return Task.FromResult(AgentSendResult.Ok(
                "accepted",
                turnId: "turn-followup-typed-001",
                turnStatus: "completed",
                correlationId: correlationId,
                requestedMode: mode,
                effectiveMode: mode));
        };

        var runner = CreateRunnerWithRuntimeFactory(runnerType, fakeRuntime);
        var (exitCode, output) = await InvokeRunnerAndCaptureOutputAsync(runner, "RunFollowUpAsync", command);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, fakeRuntime.RawRpcCallCount);
        Assert.Empty(fakeRuntime.FollowUpCalls);

        var followUpCall = Assert.Single(fakeRuntime.FollowUpInputCalls);
        Assert.Equal(FollowUpMode.Interrupt, followUpCall.Mode);
        Assert.False(string.IsNullOrWhiteSpace(followUpCall.CorrelationId));

        var textInput = Assert.IsType<TextUserInput>(Assert.Single(followUpCall.UserInputs));
        Assert.Equal("text", textInput.Type);
        Assert.Equal("继续推进", textInput.Text);

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal("thread-followup-typed-001", root.GetProperty("threadId").GetString());
        Assert.Equal("turn-followup-typed-001", root.GetProperty("turnId").GetString());
        Assert.Equal("completed", root.GetProperty("turnStatus").GetString());
        Assert.Equal(followUpCall.CorrelationId, root.GetProperty("correlationId").GetString());
        Assert.Equal("Interrupt", root.GetProperty("requestedMode").GetString());
        Assert.Equal("Interrupt", root.GetProperty("effectiveMode").GetString());
        Assert.Equal("follow-up typed 路径验证通过", root.GetProperty("assistantText").GetString());
    }

    private static object ParseCommandWithWorkspace(Type parserType, string[] args, string workingDirectory, string configPath)
    {
        var parseResult = ReflectionTestHelper.InvokeStaticMethod(parserType, "Parse", (object)args);
        var command = ReflectionTestHelper.GetProperty(parseResult!, "Command");
        Assert.NotNull(command);

        ReflectionTestHelper.SetProperty(command!, "WorkingDirectory", workingDirectory);
        ReflectionTestHelper.SetProperty(command, "ConfigFilePath", configPath);
        ReflectionTestHelper.SetProperty(command, "ProfileName", "work");
        return command;
    }

    private static async Task<(int ExitCode, string Output)> InvokeRunnerAndCaptureOutputAsync(
        object runner,
        string methodName,
        object command,
        CancellationToken cancellationToken = default)
    {
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var task = ReflectionTestHelper.InvokeMethod(runner, methodName, command, cancellationToken);
            var exitCode = await ReflectionTestHelper.AwaitTaskResultAsync(task);
            return (Assert.IsType<int>(exitCode), writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static (int ExitCode, string Output) InvokeRunnerAndCaptureOutput(object runner, string methodName, object command)
    {
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);
            var exitCode = ReflectionTestHelper.InvokeMethod(runner, methodName, command);
            return (Assert.IsType<int>(exitCode), writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static void EmitWindowsSandboxNotification(FakeAgentRuntime runtime)
    {
        runtime.EmitRawNotification(
            """
            {"method":"windowsSandbox/setupCompleted","params":{"mode":"elevated","success":true,"error":null}}
            """);
    }

    private static void EmitRealtimeStartedNotification(FakeAgentRuntime runtime)
    {
        runtime.EmitRawNotification(
            """
            {"method":"thread/realtime/started","params":{"threadId":"thread_rt_runtime","sessionId":"realtime_runtime_001"}}
            """);
    }

    private static ControlPlaneConversationStreamEvent CreateTypedStreamEvent(
        ControlPlaneConversationStreamEventKind kind,
        string? threadId = null,
        string? turnId = null,
        string? text = null,
        string? status = null)
        => new()
        {
            Kind = kind,
            ThreadId = string.IsNullOrWhiteSpace(threadId) ? null : new ThreadId(threadId),
            TurnId = string.IsNullOrWhiteSpace(turnId) ? null : new TurnId(turnId),
            Text = text,
            Status = status,
        };

    private static StructuredValue StructuredJson(string json)
        => StructuredValue.FromJsonElement(ReflectionTestHelper.ParseJsonElement(json));

    private static ControlPlaneMcpServerOauthLoginStartResult EmitMcpServerOauthLoginCompletedNotification(FakeAgentRuntime runtime)
    {
        runtime.EmitRawNotification(
            """
            {"method":"mcpServer/oauthLogin/completed","params":{"name":"demo","success":true,"error":null}}
            """);
        return new ControlPlaneMcpServerOauthLoginStartResult
        {
            AuthorizationUrl = "https://example.com/oauth/demo",
        };
    }


    private static object CreateRunnerWithRuntimeFactory(Type runnerType, IExecutionRuntime runtime)
    {
        var constructor = runnerType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(Func<IExecutionRuntime>);
            });

        return constructor.Invoke([new Func<IExecutionRuntime>(() => runtime)]);
    }

    private static string CreateWorkspace(bool createSolution, bool createKernelProject, bool createConfig, bool createAppHostProject = false)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "cli-bootstrapper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        if (createSolution)
        {
            File.WriteAllText(Path.Combine(directory, "TianShu.sln"), string.Empty, new UTF8Encoding(false));
        }

        if (createKernelProject || createAppHostProject)
        {
            var appHostProjectDirectory = Path.Combine(directory, "src", "Hosting", "TianShu.AppHost");
            Directory.CreateDirectory(appHostProjectDirectory);
            File.WriteAllText(Path.Combine(appHostProjectDirectory, "TianShu.AppHost.csproj"), "<Project />", new UTF8Encoding(false));
        }

        if (createConfig)
        {
            var configDirectory = Path.Combine(directory, ".tianshu");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(
                Path.Combine(configDirectory, "tianshu.toml"),
                """
                profile = "work"
                model = "gpt-5-codex"
                provider = "openai"
                approval_policy = "never"

                [profiles.work]
                model = "claude-3-7-sonnet"
                provider = "anthropic"
                approval_policy = "on-request"

                [providers.openai]
                base_url = "https://api.openai.com/v1"
                api_key_env = "OPENAI_API_KEY"
                default_protocol = "responses"

                [providers.anthropic]
                base_url = "https://api.anthropic.com/v1"
                api_key_env = "ANTHROPIC_API_KEY"
                default_protocol = "anthropic_messages"
                """,
                new UTF8Encoding(false));
        }

        return directory;
    }

    private static string CreateBuiltAppHostExecutable(string workingDirectory)
    {
        var executablePath = Path.Combine(
            workingDirectory,
            "src",
            "Hosting",
            "TianShu.AppHost",
            "bin",
            "Debug",
            "net10.0",
            "TianShu.AppHost.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, "stub", new UTF8Encoding(false));
        return executablePath;
    }

    private static string CreatePublishedAppHostExecutable(string workingDirectory)
    {
        var executablePath = Path.Combine(workingDirectory, "TianShu.AppHost.exe");
        File.WriteAllText(executablePath, "stub", new UTF8Encoding(false));
        return executablePath;
    }

    private static string CreateIsolatedWorkspace(bool createConfig)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tianshu-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        if (createConfig)
        {
            var configDirectory = Path.Combine(directory, ".tianshu");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(
                Path.Combine(configDirectory, "tianshu.toml"),
                """
                profile = "work"
                model = "gpt-5-codex"
                provider = "openai"
                approval_policy = "never"

                [profiles.work]
                model = "claude-3-7-sonnet"
                provider = "anthropic"
                approval_policy = "on-request"

                [providers.openai]
                base_url = "https://api.openai.com/v1"
                api_key_env = "OPENAI_API_KEY"
                default_protocol = "responses"

                [providers.anthropic]
                base_url = "https://api.anthropic.com/v1"
                api_key_env = "ANTHROPIC_API_KEY"
                default_protocol = "anthropic_messages"
                """,
                new UTF8Encoding(false));
        }

        return directory;
    }

    private sealed class FakeAgentRuntime : IExecutionRuntimeDiagnostics
    {
        public ControlPlaneInitializeRuntimeCommand? InitializedOptions { get; private set; }

        public List<(string Method, StructuredValue? Parameters)> Invocations { get; } = [];
        public List<ControlPlaneConfigReadQuery> ConfigReadRequests { get; } = [];
        public List<ControlPlaneConfigValueWriteCommand> ConfigValueWriteRequests { get; } = [];
        public List<ControlPlaneConfigBatchWriteCommand> ConfigBatchWriteRequests { get; } = [];
        public List<ControlPlaneConfigRequirementsQuery> ConfigRequirementsReadRequests { get; } = [];
        public List<GetThreadProjection> ThreadProjectionRequests { get; } = [];
        public List<GetSessionOverview> SessionOverviewRequests { get; } = [];
        public List<ListSessions> SessionListRequests { get; } = [];
        public List<ListPendingApprovals> ApprovalQueueProjectionRequests { get; } = [];
        public List<ListUserInputRequests> UserInputListRequests { get; } = [];
        public List<CreateCollaborationSpace> CollaborationSpaceCreateRequests { get; } = [];
        public List<ConfigureCollaborationSpace> CollaborationSpaceConfigureRequests { get; } = [];
        public List<ArchiveCollaborationSpace> CollaborationSpaceArchiveRequests { get; } = [];
        public List<GetCollaborationSpaceOverview> CollaborationSpaceOverviewRequests { get; } = [];
        public List<GetCollaborationSpaceProjection> CollaborationSpaceProjectionRequests { get; } = [];
        public List<ListCollaborationSpaces> CollaborationSpaceListRequests { get; } = [];
        public List<BindParticipantToSession> ParticipantSessionBindRequests { get; } = [];
        public List<BindParticipantToWorkflow> ParticipantWorkflowBindRequests { get; } = [];
        public List<UpdateParticipantRole> ParticipantRoleUpdateRequests { get; } = [];
        public List<GetParticipantProjection> ParticipantProjectionRequests { get; } = [];
        public List<GetParticipantViewProjection> ParticipantViewProjectionRequests { get; } = [];
        public List<ListParticipantsInScope> ParticipantListRequests { get; } = [];
        public List<GetArtifactDetail> ArtifactProjectionRequests { get; } = [];
        public List<ListArtifacts> ArtifactCollectionProjectionRequests { get; } = [];
        public List<CreateWorkflow> WorkflowCreateRequests { get; } = [];
        public List<PublishPlan> WorkflowPublishPlanRequests { get; } = [];
        public List<CreateTask> WorkflowCreateTaskRequests { get; } = [];
        public List<UpdateTaskState> WorkflowUpdateTaskStateRequests { get; } = [];
        public List<GetWorkflowBoard> WorkflowBoardRequests { get; } = [];
        public List<GetTaskBoard> TaskBoardRequests { get; } = [];
        public List<GetPlanProjection> PlanProjectionRequests { get; } = [];
        public List<ControlPlaneAgentListQuery> AgentListRequests { get; } = [];
        public List<GetAgentRoster> AgentRosterProjectionRequests { get; } = [];
        public List<GetTeamProjection> TeamProjectionRequests { get; } = [];
        public List<GetExecutionTrace> ExecutionTraceRequests { get; } = [];
        public List<ListAttemptSummaries> AttemptSummaryListRequests { get; } = [];
        public List<GetAccountProfile> AccountProfileRequests { get; } = [];
        public List<ListBoundDevices> BoundDeviceListRequests { get; } = [];
        public List<ListMemoryProviders> MemoryProviderListRequests { get; } = [];
        public List<ListMemorySpaces> MemorySpaceListRequests { get; } = [];
        public List<ResolveMemoryOverlay> MemoryOverlayRequests { get; } = [];
        public List<FilterMemory> MemoryFilterRequests { get; } = [];
        public List<ListMemoryReviews> MemoryReviewListRequests { get; } = [];
        public List<AddMemory> MemoryAddRequests { get; } = [];
        public List<ExtractMemory> MemoryExtractRequests { get; } = [];
        public List<ImportMemory> MemoryImportRequests { get; } = [];
        public List<ExportMemory> MemoryExportRequests { get; } = [];
        public List<BindMemoryProvider> MemoryBindProviderRequests { get; } = [];
        public List<RunMemoryConsolidation> MemoryConsolidationRequests { get; } = [];
        public List<ForgetMemory> MemoryForgetRequests { get; } = [];
        public List<DeleteMemory> MemoryDeleteRequests { get; } = [];
        public List<SupersedeMemory> MemorySupersedeRequests { get; } = [];
        public List<ApproveMemoryReview> MemoryApproveReviewRequests { get; } = [];
        public List<DemoteMemoryReview> MemoryDemoteReviewRequests { get; } = [];
        public List<MergeMemoryReview> MemoryMergeReviewRequests { get; } = [];
        public List<RestoreMemoryReview> MemoryRestoreReviewRequests { get; } = [];
        public List<RecordMemoryFeedback> MemoryFeedbackRequests { get; } = [];
        public List<RecordMemoryCitation> MemoryCitationRequests { get; } = [];
        public List<ControlPlaneModelCatalogQuery> ModelListRequests { get; } = [];
        public List<GetCapabilityCatalog> ProviderCatalogRequests { get; } = [];
        public List<ResolveEngineBinding> EngineBindingRequests { get; } = [];
        public List<ControlPlaneAppCatalogQuery> AppListRequests { get; } = [];
        public List<ControlPlaneSkillCatalogQuery> SkillsListRequests { get; } = [];
        public List<ControlPlaneSkillConfigWriteCommand> SkillsConfigWriteRequests { get; } = [];
        public List<ControlPlaneRemoteSkillCatalogQuery> SkillsRemoteListRequests { get; } = [];
        public List<ControlPlaneRemoteSkillExportCommand> SkillsRemoteExportRequests { get; } = [];
        public List<ControlPlanePluginCatalogQuery> PluginListRequests { get; } = [];
        public List<ControlPlanePluginReadQuery> PluginReadRequests { get; } = [];
        public List<ControlPlanePluginInstallCommand> PluginInstallRequests { get; } = [];
        public List<ControlPlanePluginUninstallCommand> PluginUninstallRequests { get; } = [];
        public List<ControlPlaneFeedbackUploadCommand> FeedbackUploadRequests { get; } = [];
        public List<ControlPlaneFeedbackUploadCommand> GovernanceFeedbackUploadRequests { get; } = [];
        public List<ControlPlaneFeedbackUploadCommand> DiagnosticsFeedbackUploadRequests { get; } = [];
        public List<ControlPlaneWindowsSandboxSetupStartCommand> WindowsSandboxSetupRequests { get; } = [];
        public List<ControlPlaneRegisterAgentThreadCommand> AgentThreadRegistrationCommands { get; } = [];
        public List<ControlPlaneCreateJobCommand> AgentJobCreateRequests { get; } = [];
        public List<ControlPlaneDispatchJobCommand> AgentJobDispatchRequests { get; } = [];
        public List<ControlPlaneReportJobItemCommand> AgentJobItemReportRequests { get; } = [];
        public List<ControlPlaneReadJobQuery> AgentJobReadRequests { get; } = [];
        public List<ControlPlaneReviewStartCommand> ReviewStartRequests { get; } = [];
        public List<ControlPlaneExperimentalFeatureQuery> ExperimentalFeatureListRequests { get; } = [];
        public List<ControlPlaneMcpServerStatusQuery> McpServerStatusListRequests { get; } = [];
        public List<ControlPlaneMcpServerOauthLoginStartCommand> McpServerOauthLoginRequests { get; } = [];
        public List<ControlPlaneConversationArtifactQuery> ConversationSummaryRequests { get; } = [];
        public List<ControlPlaneGitDiffArtifactQuery> GitDiffToRemoteRequests { get; } = [];
        public List<ControlPlaneThreadListQuery> ThreadListRequests { get; } = [];
        public List<(string UserMessage, IReadOnlyList<ConversationMessage> History)> SendCalls { get; } = [];
        public List<(string UserMessage, FollowUpMode Mode, string? CorrelationId)> FollowUpCalls { get; } = [];
        public List<(IReadOnlyList<AgentUserInput> UserInputs, FollowUpMode Mode, string? CorrelationId)> FollowUpInputCalls { get; } = [];
        public List<ControlPlaneMutatePendingFollowUpCommand> PendingFollowUpMutationCommands { get; } = [];
        public List<string> UserShellCommandCalls { get; } = [];
        public List<string> StartThreadCalls { get; } = [];
        public List<ControlPlaneStartThreadCommand> StartThreadRequests { get; } = [];
        public List<string> ForkThreadRequests { get; } = [];
        public List<string> ResumeThreadCalls { get; } = [];
        public List<ControlPlaneResumeThreadCommand> ResumeThreadRequests { get; } = [];
        public List<string> DeleteThreadRequests { get; } = [];
        public int ClearThreadsCallCount { get; private set; }
        public int InterruptCallCount { get; private set; }
        public int ClearDebugMemoriesCallCount { get; private set; }
        public List<ControlPlaneLoadedThreadListQuery> ThreadLoadedListRequests { get; } = [];
        public List<ControlPlaneReadThreadQuery> ThreadReadRequests { get; } = [];
        public List<(string ThreadId, int KeepRecentTurns)> CompactThreadRequests { get; } = [];
        public List<string> CleanBackgroundTerminalsRequests { get; } = [];
        public List<string> UnsubscribeThreadRequests { get; } = [];
        public List<string> IncrementThreadElicitationRequests { get; } = [];
        public List<string> DecrementThreadElicitationRequests { get; } = [];
        public List<string> UnarchiveThreadRequests { get; } = [];
        public List<ControlPlaneUpdateThreadMetadataCommand> ThreadMetadataUpdateRequests { get; } = [];
        public List<ControlPlaneRollbackThreadCommand> ThreadRollbackRequests { get; } = [];
        public List<ControlPlaneRealtimeStartCommand> RealtimeStartRequests { get; } = [];
        public List<ControlPlaneRealtimeAppendTextCommand> RealtimeAppendTextRequests { get; } = [];
        public List<ControlPlaneRealtimeAppendAudioCommand> RealtimeAppendAudioRequests { get; } = [];
        public List<ControlPlaneRealtimeHandoffOutputCommand> RealtimeHandoffOutputRequests { get; } = [];
        public List<ControlPlaneRealtimeStopCommand> RealtimeStopRequests { get; } = [];
        public List<ControlPlaneCommandExecutionStartCommand> CommandExecutionStartRequests { get; } = [];
        public List<ControlPlaneCommandExecutionWriteCommand> CommandExecutionWriteRequests { get; } = [];
        public List<ControlPlaneCommandExecutionTerminateCommand> CommandExecutionTerminateRequests { get; } = [];
        public List<ControlPlaneCommandExecutionResizeCommand> CommandExecutionResizeRequests { get; } = [];
        public List<ControlPlaneFuzzyFileSearchQuery> FuzzySearchRequests { get; } = [];
        public List<ControlPlaneStartFuzzyFileSearchSessionCommand> FuzzySessionStartRequests { get; } = [];
        public List<ControlPlaneUpdateFuzzyFileSearchSessionCommand> FuzzySessionUpdateRequests { get; } = [];
        public List<ControlPlaneStopFuzzyFileSearchSessionCommand> FuzzySessionStopRequests { get; } = [];

        public Func<string, StructuredValue?, StructuredValue>? InvokeDiagnosticRpcHandler { get; set; }
        public Func<ControlPlaneDebugClearMemoriesResult>? ClearDebugMemoriesHandler { get; set; }
        public Func<ControlPlaneConfigReadQuery, ControlPlaneConfigSnapshotResult>? ReadConfigHandler { get; set; }
        public Func<ControlPlaneConfigRequirementsQuery, ControlPlaneConfigRequirementsResult>? ReadConfigRequirementsHandler { get; set; }
        public Func<GetThreadProjection, TianShu.Contracts.Projections.ThreadProjection?>? GetThreadProjectionHandler { get; set; }
        public Func<GetSessionOverview, SessionOverviewProjection?>? GetSessionOverviewHandler { get; set; }
        public Func<ListSessions, IReadOnlyList<SessionOverviewProjection>>? ListSessionsHandler { get; set; }
        public Func<ListPendingApprovals, TianShu.Contracts.Projections.ApprovalQueueProjection?>? GetApprovalQueueProjectionHandler { get; set; }
        public Func<ListUserInputRequests, IReadOnlyList<UserInputRequest>>? ListUserInputRequestsHandler { get; set; }
        public Func<GetCollaborationSpaceOverview, CollaborationSpaceOverviewProjection?>? GetCollaborationSpaceOverviewHandler { get; set; }
        public Func<GetCollaborationSpaceProjection, TianShu.Contracts.Projections.CollaborationSpaceProjection?>? GetCollaborationSpaceProjectionHandler { get; set; }
        public Func<ListCollaborationSpaces, IReadOnlyList<CollaborationSpaceOverviewProjection>>? ListCollaborationSpacesHandler { get; set; }
        public Func<CreateCollaborationSpace, CollaborationSpace>? CreateCollaborationSpaceHandler { get; set; }
        public Func<ConfigureCollaborationSpace, CollaborationSpace>? ConfigureCollaborationSpaceHandler { get; set; }
        public Func<ArchiveCollaborationSpace, bool>? ArchiveCollaborationSpaceHandler { get; set; }
        public Func<BindParticipantToSession, bool>? BindParticipantToSessionHandler { get; set; }
        public Func<BindParticipantToWorkflow, bool>? BindParticipantToWorkflowHandler { get; set; }
        public Func<UpdateParticipantRole, bool>? UpdateParticipantRoleHandler { get; set; }
        public Func<GetParticipantProjection, ParticipantProjection?>? GetParticipantProjectionHandler { get; set; }
        public Func<GetParticipantViewProjection, TianShu.Contracts.Projections.ParticipantProjection?>? GetParticipantViewProjectionHandler { get; set; }
        public Func<ListParticipantsInScope, IReadOnlyList<ParticipantProjection>>? ListParticipantsInScopeHandler { get; set; }
        public Func<GetArtifactDetail, ArtifactProjection?>? GetArtifactProjectionHandler { get; set; }
        public Func<ListArtifacts, ArtifactCollectionProjection?>? GetArtifactCollectionProjectionHandler { get; set; }
        public Func<CreateWorkflow, Workflow>? CreateWorkflowHandler { get; set; }
        public Func<PublishPlan, PlanProjection>? PublishPlanHandler { get; set; }
        public Func<CreateTask, TianShu.Contracts.Workflows.Task>? CreateWorkflowTaskHandler { get; set; }
        public Func<UpdateTaskState, TianShu.Contracts.Workflows.Task?>? UpdateWorkflowTaskStateHandler { get; set; }
        public Func<GetWorkflowBoard, WorkflowBoardProjection?>? GetWorkflowBoardProjectionHandler { get; set; }
        public Func<GetTaskBoard, TaskBoardProjection?>? GetTaskBoardProjectionHandler { get; set; }
        public Func<GetPlanProjection, PlanProjection?>? GetPlanProjectionHandler { get; set; }
        public Func<ControlPlaneAgentListQuery, ControlPlaneAgentRosterResult>? ListAgentsHandler { get; set; }
        public Func<GetAgentRoster, AgentRosterProjection?>? GetAgentRosterProjectionHandler { get; set; }
        public Func<GetTeamProjection, TeamProjection?>? GetTeamProjectionHandler { get; set; }
        public Func<GetExecutionTrace, ExecutionTrace?>? GetExecutionTraceHandler { get; set; }
        public Func<ListAttemptSummaries, IReadOnlyList<AttemptSummary>>? ListAttemptSummariesHandler { get; set; }
        public Func<GetAccountProfile, Account?>? GetAccountProfileHandler { get; set; }
        public Func<ListBoundDevices, IReadOnlyList<DeviceBinding>>? ListBoundDevicesHandler { get; set; }
        public Func<ListMemoryProviders, IReadOnlyList<MemoryProviderDescriptor>>? ListMemoryProvidersHandler { get; set; }
        public Func<ListMemorySpaces, IReadOnlyList<MemorySpace>>? ListMemorySpacesHandler { get; set; }
        public Func<ResolveMemoryOverlay, MemoryOverlay>? ResolveMemoryOverlayHandler { get; set; }
        public Func<FilterMemory, MemoryQueryResult>? FilterMemoryHandler { get; set; }
        public Func<ListMemoryReviews, MemoryReviewQueryResult>? ListMemoryReviewsHandler { get; set; }
        public Func<AddMemory, MemoryMutationResult>? AddMemoryHandler { get; set; }
        public Func<ExtractMemory, IReadOnlyList<MemoryCandidate>>? ExtractMemoryHandler { get; set; }
        public Func<ImportMemory, MemoryMutationResult>? ImportMemoryHandler { get; set; }
        public Func<ExportMemory, MemoryQueryResult>? ExportMemoryHandler { get; set; }
        public Func<BindMemoryProvider, MemoryMutationResult>? BindMemoryProviderHandler { get; set; }
        public Func<RunMemoryConsolidation, MemoryConsolidationRunResult>? RunMemoryConsolidationHandler { get; set; }
        public Func<ForgetMemory, MemoryMutationResult>? ForgetMemoryHandler { get; set; }
        public Func<DeleteMemory, MemoryMutationResult>? DeleteMemoryHandler { get; set; }
        public Func<SupersedeMemory, MemoryMutationResult>? SupersedeMemoryHandler { get; set; }
        public Func<ApproveMemoryReview, MemoryMutationResult>? ApproveMemoryReviewHandler { get; set; }
        public Func<DemoteMemoryReview, MemoryMutationResult>? DemoteMemoryReviewHandler { get; set; }
        public Func<MergeMemoryReview, MemoryMutationResult>? MergeMemoryReviewHandler { get; set; }
        public Func<RestoreMemoryReview, MemoryMutationResult>? RestoreMemoryReviewHandler { get; set; }
        public Func<RecordMemoryFeedback, MemoryMutationResult>? RecordMemoryFeedbackHandler { get; set; }
        public Func<RecordMemoryCitation, MemoryMutationResult>? RecordMemoryCitationHandler { get; set; }
        public Func<ControlPlaneModelCatalogQuery, ControlPlaneModelCatalogResult>? ListModelsHandler { get; set; }
        public Func<GetCapabilityCatalog, CapabilityCatalogSnapshot>? ListProviderProfilesHandler { get; set; }
        public Func<ResolveEngineBinding, ResolvedEngineBinding>? ResolveEngineBindingHandler { get; set; }
        public Func<ControlPlaneAppCatalogQuery, ControlPlaneAppCatalogResult>? ListAppsHandler { get; set; }
        public Func<ControlPlaneConfigValueWriteCommand, ControlPlaneConfigWriteResult>? WriteConfigValueHandler { get; set; }
        public Func<ControlPlaneConfigBatchWriteCommand, ControlPlaneConfigWriteResult>? WriteConfigBatchHandler { get; set; }
        public Func<ControlPlaneSkillCatalogQuery, ControlPlaneSkillCatalogResult>? ListSkillsHandler { get; set; }
        public Func<ControlPlaneSkillConfigWriteCommand, ControlPlaneSkillConfigWriteResult>? WriteSkillsConfigHandler { get; set; }
        public Func<ControlPlaneRemoteSkillCatalogQuery, ControlPlaneRemoteSkillCatalogResult>? ListRemoteSkillsHandler { get; set; }
        public Func<ControlPlaneRemoteSkillExportCommand, ControlPlaneRemoteSkillExportResult>? ExportRemoteSkillHandler { get; set; }
        public Func<ControlPlanePluginCatalogQuery, ControlPlanePluginCatalogResult>? ListPluginsHandler { get; set; }
        public Func<ControlPlanePluginReadQuery, ControlPlanePluginReadResult>? ReadPluginHandler { get; set; }
        public Func<ControlPlanePluginInstallCommand, ControlPlanePluginInstallResult>? InstallPluginHandler { get; set; }
        public Func<ControlPlanePluginUninstallCommand, ControlPlanePluginUninstallResult>? UninstallPluginHandler { get; set; }
        public Func<ControlPlaneFeedbackUploadCommand, ControlPlaneFeedbackUploadResult>? UploadFeedbackHandler { get; set; }
        public Func<ControlPlaneWindowsSandboxSetupStartCommand, ControlPlaneWindowsSandboxSetupStartResult>? StartWindowsSandboxSetupHandler { get; set; }
        public Func<ControlPlaneRegisterAgentThreadCommand, ControlPlaneAgentThreadRegistrationResult>? RegisterAgentThreadHandler { get; set; }
        public Func<ControlPlaneCreateJobCommand, ControlPlaneJobOperationResult>? CreateAgentJobHandler { get; set; }
        public Func<ControlPlaneDispatchJobCommand, ControlPlaneJobOperationResult>? DispatchAgentJobHandler { get; set; }
        public Func<ControlPlaneReportJobItemCommand, ControlPlaneJobOperationResult>? ReportAgentJobItemHandler { get; set; }
        public Func<ControlPlaneReadJobQuery, ControlPlaneJobOperationResult>? ReadAgentJobHandler { get; set; }
        public Func<ControlPlaneListJobsQuery, ControlPlaneJobListResult>? ListAgentJobsHandler { get; set; }
        public Func<ControlPlaneReviewStartCommand, ControlPlaneReviewStartResult>? StartReviewHandler { get; set; }
        public Func<ControlPlaneExperimentalFeatureQuery, ControlPlaneExperimentalFeatureCatalogResult>? ListExperimentalFeaturesHandler { get; set; }
        public Func<ControlPlaneCollaborationModeCatalogResult>? ListCollaborationModesHandler { get; set; }
        public Func<ControlPlaneMcpServerStatusQuery, ControlPlaneMcpServerCatalogResult>? ListMcpServerStatusHandler { get; set; }
        public Func<ControlPlaneMcpServerReloadResult>? ReloadMcpServersHandler { get; set; }
        public Func<ControlPlaneProviderPackageReloadResult>? ReloadProviderPackagesHandler { get; set; }
        public Func<ControlPlaneMcpServerOauthLoginStartCommand, ControlPlaneMcpServerOauthLoginStartResult>? StartMcpServerOauthLoginHandler { get; set; }
        public Func<ControlPlaneConversationArtifactQuery, ControlPlaneConversationArtifact?>? GetConversationSummaryHandler { get; set; }
        public Func<ControlPlaneGitDiffArtifactQuery, ControlPlaneGitDiffArtifact>? GetGitDiffToRemoteHandler { get; set; }
        public Func<ControlPlaneThreadListQuery, AgentThreadListResult>? ListThreadsHandler { get; set; }
        public Func<CancellationToken, Task<AgentThreadInfo?>>? StartNewThreadAsyncHandler { get; set; }
        public Func<ControlPlaneStartThreadCommand, AgentThreadInfo?>? StartNewThreadRequestHandler { get; set; }
        public Func<string, AgentThreadInfo?>? ForkThreadHandler { get; set; }
        public Func<string, CancellationToken, Task<AgentThreadResumeResult?>>? ResumeThreadAsyncHandler { get; set; }
        public Func<ControlPlaneResumeThreadCommand, AgentThreadResumeResult?>? ResumeThreadRequestHandler { get; set; }
        public Func<string, bool>? DeleteThreadHandler { get; set; }
        public Func<ControlPlaneClearThreadsResult>? ClearThreadsHandler { get; set; }
        public Func<string, IReadOnlyList<ConversationMessage>, CancellationToken, Task<AgentSendResult>>? SendAsyncHandler { get; set; }
        public Func<string, FollowUpMode, CancellationToken, string?, Task<AgentSendResult>>? SendFollowUpAsyncHandler { get; set; }
        public Func<IReadOnlyList<AgentUserInput>, FollowUpMode, CancellationToken, string?, Task<AgentSendResult>>? SendFollowUpInputsAsyncHandler { get; set; }
        public Func<string, CancellationToken, Task<AgentSendResult>>? RunUserShellCommandAsyncHandler { get; set; }
        public Func<ControlPlaneLoadedThreadListQuery, ControlPlaneLoadedThreadListResult>? ListLoadedThreadsHandler { get; set; }
        public Func<ControlPlaneReadThreadQuery, ControlPlaneThreadOperationResult>? ReadThreadHandler { get; set; }
        public Func<string, int, ControlPlaneThreadCommandAcceptedResult>? CompactThreadHandler { get; set; }
        public Func<string, ControlPlaneThreadCommandAcceptedResult>? CleanBackgroundTerminalsHandler { get; set; }
        public Func<string, ControlPlaneThreadUnsubscribeResult>? UnsubscribeThreadHandler { get; set; }
        public Func<string, ControlPlaneThreadElicitationResult>? IncrementThreadElicitationHandler { get; set; }
        public Func<string, ControlPlaneThreadElicitationResult>? DecrementThreadElicitationHandler { get; set; }
        public Func<string, ControlPlaneThreadOperationResult>? UnarchiveThreadHandler { get; set; }
        public Func<ControlPlaneUpdateThreadMetadataCommand, ControlPlaneThreadOperationResult>? UpdateThreadMetadataHandler { get; set; }
        public Func<ControlPlaneRollbackThreadCommand, ControlPlaneThreadOperationResult>? RollbackThreadHandler { get; set; }
        public Func<ControlPlaneRealtimeStartCommand, ControlPlaneRealtimeCommandAcceptedResult>? StartRealtimeHandler { get; set; }
        public Func<ControlPlaneRealtimeAppendTextCommand, ControlPlaneRealtimeCommandAcceptedResult>? AppendRealtimeTextHandler { get; set; }
        public Func<ControlPlaneRealtimeAppendAudioCommand, ControlPlaneRealtimeCommandAcceptedResult>? AppendRealtimeAudioHandler { get; set; }
        public Func<ControlPlaneRealtimeHandoffOutputCommand, ControlPlaneRealtimeCommandAcceptedResult>? HandoffRealtimeOutputHandler { get; set; }
        public Func<ControlPlaneRealtimeStopCommand, ControlPlaneRealtimeCommandAcceptedResult>? StopRealtimeHandler { get; set; }
        public Func<ControlPlaneCommandExecutionStartCommand, ControlPlaneCommandExecutionResult>? StartCommandExecutionHandler { get; set; }
        public Func<ControlPlaneCommandExecutionWriteCommand, ControlPlaneCommandExecutionCommandAcceptedResult>? WriteCommandExecutionHandler { get; set; }
        public Func<ControlPlaneCommandExecutionTerminateCommand, ControlPlaneCommandExecutionCommandAcceptedResult>? TerminateCommandExecutionHandler { get; set; }
        public Func<ControlPlaneCommandExecutionResizeCommand, ControlPlaneCommandExecutionCommandAcceptedResult>? ResizeCommandExecutionHandler { get; set; }
        public Func<ControlPlaneFuzzyFileSearchQuery, ControlPlaneFuzzyFileSearchResult>? SearchFuzzyFilesHandler { get; set; }
        public Func<ControlPlaneStartFuzzyFileSearchSessionCommand, ControlPlaneFuzzyFileSearchCommandAcceptedResult>? StartFuzzyFileSearchSessionHandler { get; set; }
        public Func<ControlPlaneUpdateFuzzyFileSearchSessionCommand, ControlPlaneFuzzyFileSearchCommandAcceptedResult>? UpdateFuzzyFileSearchSessionHandler { get; set; }
        public Func<ControlPlaneStopFuzzyFileSearchSessionCommand, ControlPlaneFuzzyFileSearchCommandAcceptedResult>? StopFuzzyFileSearchSessionHandler { get; set; }

        public int DiagnosticRpcCallCount { get; private set; }
        public int RawRpcCallCount => DiagnosticRpcCallCount;

        public string RuntimeName => "FakeAgentRuntime";

        public string? ActiveThreadId { get; set; } = "fake-thread";

        public bool HasActiveTurn => false;

        private event EventHandler<ControlPlaneConversationStreamEventArgs>? InternalStreamEventReceived;

        public event EventHandler<ControlPlaneConversationStreamEventArgs>? StreamEventReceived
        {
            add => InternalStreamEventReceived += value;
            remove => InternalStreamEventReceived -= value;
        }

        public void EmitStreamEvent(ControlPlaneConversationStreamEvent streamEvent)
            => InternalStreamEventReceived?.Invoke(this, new ControlPlaneConversationStreamEventArgs(streamEvent));

        public Task InitializeAsync(
            ControlPlaneInitializeRuntimeCommand options,
            Func<ToolInvocationRequest, CancellationToken, Task<ToolInvocationResult>>? dynamicToolCallHandler,
            CancellationToken cancellationToken)
        {
            InitializedOptions = options;
            return Task.CompletedTask;
        }

        public Task<ExecutionRunResult> ExecuteAsync(ExecutionPlan plan, ExecutionRuntimeContext context, CancellationToken cancellationToken)
            => Task.FromException<ExecutionRunResult>(new NotSupportedException("FakeAgentRuntime 不执行 RuntimeStep。"));

        public Task<RuntimeStepResult> ExecuteStepAsync(RuntimeStep step, ExecutionRuntimeContext context, CancellationToken cancellationToken)
            => Task.FromException<RuntimeStepResult>(new NotSupportedException("FakeAgentRuntime 不执行 RuntimeStep。"));

        public Task<ControlPlaneTurnSubmissionResult> SendAsync(ControlPlaneSubmitTurnCommand command, CancellationToken cancellationToken)
        {
            var userInputs = ToAgentUserInputs(command.Inputs);
            var history = ToConversationHistory(command.History);
            var preview = BuildUserInputPreview(userInputs);
            SendCalls.Add((preview, history));
            return MapTurnSubmissionResultAsync(
                SendAsyncHandler?.Invoke(preview, history, cancellationToken)
                ?? Task.FromResult(AgentSendResult.Fail("SendAsyncHandler 未配置。")));
        }

        public Task<ControlPlaneTurnSubmissionResult> RunUserShellCommandAsync(string command, CancellationToken cancellationToken)
        {
            UserShellCommandCalls.Add(command);
            return MapTurnSubmissionResultAsync(
                RunUserShellCommandAsyncHandler?.Invoke(command, cancellationToken)
                ?? Task.FromResult(AgentSendResult.Fail("RunUserShellCommandAsyncHandler 未配置。")));
        }

        public Task<ControlPlaneTurnSubmissionResult> SendFollowUpAsync(ControlPlaneSubmitFollowUpCommand command, CancellationToken cancellationToken)
        {
            var mode = ToRuntimeFollowUpMode(command.Mode);
            var userInputs = ToAgentUserInputs(command.Inputs);
            var preview = BuildUserInputPreview(userInputs);
            Task<AgentSendResult> task;
            if (SendFollowUpInputsAsyncHandler is not null)
            {
                FollowUpInputCalls.Add((userInputs, mode, command.CorrelationId));
                task = SendFollowUpInputsAsyncHandler(userInputs, mode, cancellationToken, command.CorrelationId);
            }
            else
            {
                FollowUpCalls.Add((preview, mode, command.CorrelationId));
                task = SendFollowUpAsyncHandler?.Invoke(preview, mode, cancellationToken, command.CorrelationId)
                    ?? Task.FromResult(AgentSendResult.Fail("SendFollowUpAsyncHandler 未配置。"));
            }

            return MapTurnSubmissionResultAsync(task);
        }

        public Task<ControlPlanePendingFollowUpMutationResult> MutatePendingFollowUpAsync(ControlPlaneMutatePendingFollowUpCommand command, CancellationToken cancellationToken)
        {
            PendingFollowUpMutationCommands.Add(command);
            return Task.FromResult(new ControlPlanePendingFollowUpMutationResult
            {
                Accepted = true,
                Message = "已处理待发送项。",
                CorrelationId = command.CorrelationId,
                Kind = command.Kind,
            });
        }

        public Task InterruptTurnAsync(CancellationToken cancellationToken)
        {
            InterruptCallCount++;
            return Task.CompletedTask;
        }

        public Task<bool> RespondToApprovalAsync(ControlPlaneApprovalResolution command, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<bool> RespondToPermissionRequestAsync(ControlPlanePermissionGrant command, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<bool> RespondToUserInputAsync(ControlPlaneUserInputSubmission command, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<TianShu.Contracts.Projections.ApprovalQueueProjection?> GetApprovalQueueProjectionAsync(ListPendingApprovals query, CancellationToken cancellationToken)
        {
            ApprovalQueueProjectionRequests.Add(query);
            return Task.FromResult(GetApprovalQueueProjectionHandler?.Invoke(query));
        }

        public Task<IReadOnlyList<UserInputRequest>> ListUserInputRequestsAsync(ListUserInputRequests query, CancellationToken cancellationToken)
        {
            UserInputListRequests.Add(query);
            return Task.FromResult(ListUserInputRequestsHandler?.Invoke(query) ?? Array.Empty<UserInputRequest>());
        }

        public Task<ControlPlaneThreadListResult> ListThreadsAsync(ControlPlaneThreadListQuery query, CancellationToken cancellationToken)
        {
            ThreadListRequests.Add(query);
            return Task.FromResult(ToControlPlaneThreadListResult(ListThreadsHandler?.Invoke(query) ?? new AgentThreadListResult()));
        }

        public Task<TianShu.Contracts.Projections.ThreadProjection?> GetThreadProjectionAsync(GetThreadProjection query, CancellationToken cancellationToken)
        {
            ThreadProjectionRequests.Add(query);
            return Task.FromResult(GetThreadProjectionHandler?.Invoke(query));
        }

        public Task<PlanProjection?> GetPlanProjectionAsync(GetPlanProjection request, CancellationToken cancellationToken)
        {
            PlanProjectionRequests.Add(request);
            return Task.FromResult(GetPlanProjectionHandler?.Invoke(request));
        }

        public Task<TeamProjection?> GetTeamProjectionAsync(GetTeamProjection request, CancellationToken cancellationToken)
        {
            TeamProjectionRequests.Add(request);
            return Task.FromResult(GetTeamProjectionHandler?.Invoke(request));
        }

        public Task<AgentRosterProjection?> GetAgentRosterProjectionAsync(GetAgentRoster request, CancellationToken cancellationToken)
        {
            AgentRosterProjectionRequests.Add(request);
            return Task.FromResult(
                GetAgentRosterProjectionHandler?.Invoke(request)
                ?? (request.WorkflowId is null
                    ? new AgentRosterProjection(
                    [
                        new AgentRosterEntry(
                            new AgentId("agent-fake"),
                            new ParticipantRef(new ParticipantId("agent-fake"), ParticipantKind.Agent, "Fake Agent"),
                            "member",
                            0,
                            false),
                    ])
                    : null));
        }

        public Task<ControlPlaneAgentRosterResult> ListAgentsAsync(ControlPlaneAgentListQuery request, CancellationToken cancellationToken)
        {
            AgentListRequests.Add(request);
            return Task.FromResult(ListAgentsHandler?.Invoke(request) ?? new ControlPlaneAgentRosterResult
            {
                Agents =
                [
                    new ControlPlaneAgentDescriptor
                    {
                        ThreadId = new ThreadId("agent-fake"),
                        AgentNickname = "Fake Agent",
                        AgentRole = "member",
                        UpdatedAt = DateTimeOffset.UtcNow,
                    },
                ],
            });
        }

        public Task<WorkflowBoardProjection?> GetWorkflowBoardProjectionAsync(GetWorkflowBoard request, CancellationToken cancellationToken)
        {
            WorkflowBoardRequests.Add(request);
            return Task.FromResult(GetWorkflowBoardProjectionHandler?.Invoke(request));
        }

        public Task<TaskBoardProjection?> GetTaskBoardProjectionAsync(GetTaskBoard request, CancellationToken cancellationToken)
        {
            TaskBoardRequests.Add(request);
            return Task.FromResult(GetTaskBoardProjectionHandler?.Invoke(request));
        }

        public Task<Artifact> PublishArtifactAsync(PublishArtifact request, CancellationToken cancellationToken)
            => Task.FromResult(request.Artifact);

        public Task<Artifact> PromoteArtifactAsync(PromoteArtifact request, CancellationToken cancellationToken)
            => Task.FromResult(new Artifact(
                request.ArtifactId,
                new CollaborationSpaceRef(new CollaborationSpaceId("space-fake"), "space-fake", "Fake Space"),
                "fake-artifact.md",
                ArtifactKind.Document,
                state: ArtifactLifecycleState.Promoted));

        public Task<Artifact> AttachArtifactToTaskAsync(AttachArtifactToTask request, CancellationToken cancellationToken)
            => Task.FromResult(new Artifact(
                request.ArtifactId,
                new CollaborationSpaceRef(new CollaborationSpaceId("space-fake"), "space-fake", "Fake Space"),
                "fake-artifact.md",
                ArtifactKind.Document,
                state: ArtifactLifecycleState.Published));

        public Task<ArtifactProjection?> GetArtifactProjectionAsync(GetArtifactDetail request, CancellationToken cancellationToken)
        {
            ArtifactProjectionRequests.Add(request);
            return Task.FromResult(GetArtifactProjectionHandler?.Invoke(request));
        }

        public Task<ArtifactCollectionProjection?> GetArtifactCollectionProjectionAsync(ListArtifacts request, CancellationToken cancellationToken)
        {
            ArtifactCollectionProjectionRequests.Add(request);
            return Task.FromResult(GetArtifactCollectionProjectionHandler?.Invoke(request));
        }

        public async Task<IReadOnlyList<ControlPlaneThreadSummary>> ListThreadsAsync(int limit, bool archived, bool matchCurrentCwd, CancellationToken cancellationToken)
        {
            var result = await ListThreadsAsync(
                    new ControlPlaneThreadListQuery
                    {
                        Limit = limit,
                        Archived = archived,
                        WorkingDirectory = matchCurrentCwd ? "D:/Work/TianShu" : null,
                        SortKey = "updated_at",
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return result.Threads;
        }

        public async Task<ControlPlaneThreadSummary?> StartNewThreadAsync(CancellationToken cancellationToken)
        {
            StartThreadCalls.Add("start");
            var result = StartNewThreadAsyncHandler is null
                ? null
                : await StartNewThreadAsyncHandler(cancellationToken).ConfigureAwait(false);
            return ToControlPlaneThreadSummary(result);
        }

        public Task<ControlPlaneThreadSummary?> StartNewThreadAsync(ControlPlaneStartThreadCommand command, CancellationToken cancellationToken)
        {
            StartThreadRequests.Add(command);
            if (StartNewThreadRequestHandler is not null)
            {
                return Task.FromResult(ToControlPlaneThreadSummary(StartNewThreadRequestHandler.Invoke(command)));
            }

            return StartNewThreadAsync(cancellationToken);
        }

        public Task<ControlPlaneThreadSummary?> ForkThreadAsync(string threadId, CancellationToken cancellationToken)
        {
            ForkThreadRequests.Add(threadId);
            return Task.FromResult(ToControlPlaneThreadSummary(ForkThreadHandler?.Invoke(threadId)));
        }

        public async Task<ControlPlaneThreadSummary?> ForkThreadAsync(ControlPlaneForkThreadCommand command, CancellationToken cancellationToken)
            => await ForkThreadAsync(command.ThreadId.Value, cancellationToken).ConfigureAwait(false);

        public Task<bool> ArchiveThreadAsync(string threadId, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<bool> DeleteThreadAsync(string threadId, CancellationToken cancellationToken)
        {
            DeleteThreadRequests.Add(threadId);
            return Task.FromResult(DeleteThreadHandler?.Invoke(threadId) ?? false);
        }

        public Task<ControlPlaneClearThreadsResult> ClearThreadsAsync(CancellationToken cancellationToken)
        {
            ClearThreadsCallCount++;
            return Task.FromResult(ClearThreadsHandler?.Invoke() ?? new ControlPlaneClearThreadsResult());
        }

        public Task<bool> RenameThreadAsync(string threadId, string name, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public async Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
        {
            ResumeThreadCalls.Add(threadId);
            var result = ResumeThreadAsyncHandler is null
                ? null
                : await ResumeThreadAsyncHandler(threadId, cancellationToken).ConfigureAwait(false);
            return ToControlPlaneThreadSnapshot(result);
        }

        public Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(ControlPlaneResumeThreadCommand command, CancellationToken cancellationToken)
        {
            ResumeThreadRequests.Add(command);
            if (ResumeThreadRequestHandler is not null)
            {
                return Task.FromResult(ToControlPlaneThreadSnapshot(ResumeThreadRequestHandler.Invoke(command)));
            }

            return ResumeThreadAsync(command.ThreadId.Value, cancellationToken);
        }

        private static ControlPlaneThreadListResult ToControlPlaneThreadListResult(AgentThreadListResult result)
            => new()
            {
                Threads = result.Data.Select(ToControlPlaneThreadSummary).Where(static item => item is not null).Cast<ControlPlaneThreadSummary>().ToArray(),
                NextCursor = result.NextCursor,
            };

        private static ControlPlaneThreadSummary? ToControlPlaneThreadSummary(AgentThreadInfo? thread)
            => thread is null
                ? null
                : new ControlPlaneThreadSummary
                {
                    ThreadId = new ThreadId(thread.ThreadId),
                    Preview = thread.Preview,
                    Name = thread.Name,
                    WorkingDirectory = thread.Cwd,
                    Path = thread.Path,
                    ModelProvider = thread.ModelProvider,
                    Source = thread.Source?.GetThreadSourceKind(),
                    ParentThreadId = thread.Source?.SubAgentSource is { ParentThreadId: { Length: > 0 } parentThreadId }
                        ? new ThreadId(parentThreadId)
                        : null,
                    LineageDepth = thread.Source?.SubAgentSource?.Depth,
                    CliVersion = thread.CliVersion,
                    AgentNickname = thread.AgentNickname,
                    AgentRole = thread.AgentRole,
                    CreatedAt = thread.CreatedAt,
                    UpdatedAt = thread.UpdatedAt,
                    IsEphemeral = thread.IsEphemeral,
                    Status = thread.Status?.Type,
                    ActiveFlags = thread.Status?.ActiveFlags ?? Array.Empty<string>(),
                    GitSha = thread.GitInfo?.Sha,
                    GitBranch = thread.GitInfo?.Branch,
                    GitOriginUrl = thread.GitInfo?.OriginUrl,
                    SessionConfiguration = ToControlPlaneThreadSessionConfiguration(thread.SessionConfiguration),
                };

        private static ControlPlaneThreadSnapshot? ToControlPlaneThreadSnapshot(AgentThreadResumeResult? result)
            => result is null
                ? null
                : new ControlPlaneThreadSnapshot
                {
                    Thread = new ControlPlaneThreadSummary
                    {
                        ThreadId = new ThreadId(result.ThreadId),
                        Preview = result.Preview,
                        Name = result.Name,
                        WorkingDirectory = result.Cwd,
                        Path = result.Path,
                        ModelProvider = result.ModelProvider,
                        Source = result.Source?.GetThreadSourceKind(),
                        ParentThreadId = result.Source?.SubAgentSource is { ParentThreadId: { Length: > 0 } parentThreadId }
                            ? new ThreadId(parentThreadId)
                            : null,
                        LineageDepth = result.Source?.SubAgentSource?.Depth,
                        CliVersion = result.CliVersion,
                        AgentNickname = result.AgentNickname,
                        AgentRole = result.AgentRole,
                        CreatedAt = result.CreatedAt,
                        UpdatedAt = result.UpdatedAt,
                        IsEphemeral = result.IsEphemeral,
                        Status = result.Status?.Type,
                        ActiveFlags = result.Status?.ActiveFlags ?? Array.Empty<string>(),
                        GitSha = result.GitInfo?.Sha,
                        GitBranch = result.GitInfo?.Branch,
                        GitOriginUrl = result.GitInfo?.OriginUrl,
                        SessionConfiguration = ToControlPlaneThreadSessionConfiguration(result.SessionConfiguration),
                    },
                    Messages = result.Messages.Select(ToControlPlaneConversationMessage).ToArray(),
                    Turns = result.Turns.Select(ToControlPlaneThreadTurn).ToArray(),
                    SeedHistory = result.SeedHistory.Select(ToControlPlaneSeedHistoryItem).ToArray(),
                    PendingInputState = result.PendingInputState,
                    PendingInteractiveRequests = result.PendingInteractiveRequests.ToArray(),
                };

        private static ControlPlaneThreadSessionConfiguration? ToControlPlaneThreadSessionConfiguration(AgentThreadSessionConfiguration? configuration)
            => configuration is null
                ? null
                : new ControlPlaneThreadSessionConfiguration
                {
                    Model = configuration.Model,
                    ModelProvider = configuration.ModelProvider,
                    ModelProviderId = configuration.ModelProviderId,
                    ServiceTier = configuration.ServiceTier?.ToString(),
                    ApprovalPolicy = configuration.ApprovalPolicy?.ToString(),
                    SandboxPolicy = configuration.SandboxPolicy,
                    SandboxPolicyPayload = configuration.SandboxPolicyPayload is null ? null : StructuredValue.FromPlainObject(configuration.SandboxPolicyPayload.ToPlainObject()),
                    ReasoningEffort = configuration.ReasoningEffort,
                    HistoryLogId = configuration.HistoryLogId,
                    HistoryEntryCount = configuration.HistoryEntryCount,
                    RolloutPath = configuration.RolloutPath,
                    ReasoningSummary = configuration.ReasoningSummary,
                    Verbosity = configuration.Verbosity,
                    Personality = configuration.Personality,
                    AllowLoginShell = configuration.AllowLoginShell,
                    ShellEnvironmentPolicy = configuration.ShellEnvironmentPolicy is null ? null : StructuredValue.FromPlainObject(configuration.ShellEnvironmentPolicy.ToPlainObject()),
                    ProviderBaseUrl = configuration.ProviderBaseUrl,
                    ProviderApiKeyEnvironmentVariable = configuration.ProviderApiKeyEnvironmentVariable,
                    ProviderWireApi = configuration.ProviderWireApi,
                    ProviderRequestMaxRetries = configuration.ProviderRequestMaxRetries,
                    ProviderStreamMaxRetries = configuration.ProviderStreamMaxRetries,
                    ProviderStreamIdleTimeoutMs = configuration.ProviderStreamIdleTimeoutMs,
                    ProviderWebsocketConnectTimeoutMs = configuration.ProviderWebsocketConnectTimeoutMs,
                    ProviderSupportsWebsockets = configuration.ProviderSupportsWebsockets,
                    WebSearchMode = configuration.WebSearchMode,
                    ServiceName = configuration.ServiceName,
                    BaseInstructions = configuration.BaseInstructions,
                    DeveloperInstructions = configuration.DeveloperInstructions,
                    UserInstructions = configuration.UserInstructions,
                    DynamicTools = configuration.DynamicTools?.Select(static item => StructuredValue.FromPlainObject(item.ToPlainObject())).ToArray(),
                    CollaborationMode = configuration.CollaborationMode is null ? null : StructuredValue.FromPlainObject(configuration.CollaborationMode.ToPlainObject()),
                    PersistExtendedHistory = configuration.PersistExtendedHistory,
                    ForkedFromThreadId = string.IsNullOrWhiteSpace(configuration.ForkedFromId) ? null : new ThreadId(configuration.ForkedFromId),
                    WorkingDirectory = configuration.Cwd,
                    SessionSource = configuration.SessionSource,
                    WindowsSandboxLevel = configuration.WindowsSandboxLevel,
                    DefaultModeRequestUserInputEnabled = configuration.DefaultModeRequestUserInputEnabled,
                };

        private static ControlPlaneConversationMessage ToControlPlaneConversationMessage(ConversationMessage message)
            => new()
            {
                Role = message.Role switch
                {
                    ConversationRole.System => ControlPlaneConversationRole.System,
                    ConversationRole.Assistant => ControlPlaneConversationRole.Assistant,
                    _ => ControlPlaneConversationRole.User,
                },
                Content = message.Content,
                ContentItems = message.ContentItems.Select(ToControlPlaneInputItem).ToArray(),
                Timestamp = message.Timestamp,
                IsStreaming = message.IsStreaming,
            };

        private static ControlPlaneInputItem ToControlPlaneInputItem(AgentUserInput input)
            => input switch
            {
                TextUserInput text => new ControlPlaneTextInput(
                    text.Text,
                    text.TextElements.Select(static element => new ControlPlaneTextElement(
                        new ControlPlaneByteRange(element.ByteRange.Start, element.ByteRange.End),
                        element.Placeholder)).ToArray()),
                ImageUserInput image => new ControlPlaneImageInput(image.Url),
                LocalImageUserInput localImage => new ControlPlaneLocalImageInput(localImage.Path),
                SkillUserInput skill => new ControlPlaneSkillInput(skill.Name, skill.Path),
                MentionUserInput mention => new ControlPlaneMentionInput(mention.Name, mention.Path),
                _ => new ControlPlaneTextInput(input.Type),
            };

        private static IReadOnlyList<AgentUserInput> ToAgentUserInputs(IReadOnlyList<ControlPlaneInputItem> inputs)
            => inputs.Select(ToAgentUserInput).ToArray();

        private static AgentUserInput ToAgentUserInput(ControlPlaneInputItem input)
            => input switch
            {
                ControlPlaneTextInput text => new TextUserInput
                {
                    Type = text.Type,
                    Text = text.Text,
                    TextElements = (text.TextElements ?? Array.Empty<ControlPlaneTextElement>())
                        .Select(static element => new AgentTextElement
                        {
                            ByteRange = new AgentByteRange
                            {
                                Start = element.ByteRange.Start,
                                End = element.ByteRange.End,
                            },
                            Placeholder = element.Placeholder,
                        })
                        .ToArray(),
                },
                ControlPlaneImageInput image => new ImageUserInput
                {
                    Type = image.Type,
                    Url = image.Url,
                },
                ControlPlaneLocalImageInput localImage => new LocalImageUserInput
                {
                    Type = localImage.Type,
                    Path = localImage.Path,
                },
                ControlPlaneSkillInput skill => new SkillUserInput
                {
                    Type = skill.Type,
                    Name = skill.Name,
                    Path = skill.Path,
                },
                ControlPlaneMentionInput mention => new MentionUserInput
                {
                    Type = mention.Type,
                    Name = mention.Name,
                    Path = mention.Path,
                },
                _ => throw new NotSupportedException($"不支持的控制平面输入类型：{input.GetType().Name}"),
            };

        private static IReadOnlyList<ConversationMessage> ToConversationHistory(IReadOnlyList<ControlPlaneConversationMessage> history)
            => history.Select(static message => new ConversationMessage
            {
                Role = message.Role switch
                {
                    ControlPlaneConversationRole.System => ConversationRole.System,
                    ControlPlaneConversationRole.Assistant => ConversationRole.Assistant,
                    _ => ConversationRole.User,
                },
                Content = message.Content,
                ContentItems = ToAgentUserInputs(message.ContentItems),
                Timestamp = message.Timestamp,
                IsStreaming = message.IsStreaming,
            }).ToArray();

        private static string BuildUserInputPreview(IReadOnlyList<AgentUserInput> userInputs)
            => string.Join(
                Environment.NewLine,
                userInputs
                    .Select(static input => input switch
                    {
                        TextUserInput text => text.Text,
                        LocalImageUserInput image => image.Path,
                        ImageUserInput image => image.Url,
                        SkillUserInput skill => skill.Name,
                        MentionUserInput mention => mention.Name,
                        _ => string.Empty,
                    })
                    .Where(static value => !string.IsNullOrWhiteSpace(value)));

        private static FollowUpMode ToRuntimeFollowUpMode(ControlPlaneFollowUpMode mode)
            => mode switch
            {
                ControlPlaneFollowUpMode.Queue => FollowUpMode.Queue,
                ControlPlaneFollowUpMode.Steer => FollowUpMode.Steer,
                ControlPlaneFollowUpMode.Interrupt => FollowUpMode.Interrupt,
                _ => FollowUpMode.Queue,
            };

        private static async Task<ControlPlaneTurnSubmissionResult> MapTurnSubmissionResultAsync(Task<AgentSendResult> task)
        {
            var result = await task.ConfigureAwait(false);
            return new ControlPlaneTurnSubmissionResult
            {
                Accepted = result.Success,
                Message = result.Message,
                TurnId = string.IsNullOrWhiteSpace(result.TurnId) ? null : new TurnId(result.TurnId),
                TurnStatus = result.TurnStatus,
                CorrelationId = result.CorrelationId,
                RequestedMode = result.RequestedMode switch
                {
                    FollowUpMode.Queue => ControlPlaneFollowUpMode.Queue,
                    FollowUpMode.Steer => ControlPlaneFollowUpMode.Steer,
                    FollowUpMode.Interrupt => ControlPlaneFollowUpMode.Interrupt,
                    _ => null,
                },
                EffectiveMode = result.EffectiveMode switch
                {
                    FollowUpMode.Queue => ControlPlaneFollowUpMode.Queue,
                    FollowUpMode.Steer => ControlPlaneFollowUpMode.Steer,
                    FollowUpMode.Interrupt => ControlPlaneFollowUpMode.Interrupt,
                    _ => null,
                },
            };
        }

        private static ControlPlaneThreadTurn ToControlPlaneThreadTurn(AgentThreadTurn turn)
            => new()
            {
                Id = turn.Id,
                Status = turn.Status,
                Error = turn.Error is null
                    ? null
                    : new ControlPlaneThreadTurnError
                    {
                        Message = turn.Error.Message,
                        AdditionalDetails = turn.Error.AdditionalDetails,
                    },
                Items = turn.Items.Select(ToControlPlaneThreadTurnItem).ToArray(),
            };

        private static ControlPlaneThreadTurnItem ToControlPlaneThreadTurnItem(AgentThreadTurnItem item)
            => new()
            {
                Id = item.Id,
                Type = item.Type,
                Text = item.Text,
                Phase = item.Phase,
                Data = item is GenericThreadTurnItem generic && generic.RawData is not null
                    ? StructuredValue.FromPlainObject(generic.RawData.ToPlainObject())
                    : null,
            };

        private static ControlPlaneSeedHistoryItem ToControlPlaneSeedHistoryItem(AgentThreadSeedHistoryItem item)
            => new()
            {
                Role = item.Role,
                Content = item.Content,
                Inputs = item.Inputs.Select(ToControlPlaneInputItem).ToArray(),
            };

        private static ControlPlanePendingInputState? ToControlPlanePendingInputState(PendingInputStatePayload? payload)
            => payload is null
                ? null
                : new ControlPlanePendingInputState(
                    payload.Entries.Select(ToControlPlanePendingInputStateEntry).ToArray(),
                    payload.InterruptRequestPending,
                    payload.SubmitPendingSteersAfterInterrupt,
                    payload.QueuedUserMessages?.Select(ToControlPlanePendingInputStateEntry).ToArray(),
                    payload.PendingSteers?.Select(ToControlPlanePendingInputStateEntry).ToArray());

        private static ControlPlanePendingInputStateEntry ToControlPlanePendingInputStateEntry(PendingInputStateEntryPayload entry)
            => new(
                entry.CorrelationId,
                entry.RequestedMode,
                entry.EffectiveMode,
                entry.LifecycleState,
                entry.ExpectedTurnId,
                entry.TurnId,
                entry.CompareKey is null
                    ? null
                    : StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["message"] = entry.CompareKey.Message,
                        ["imageCount"] = entry.CompareKey.ImageCount,
                    }),
                entry.PendingBucket,
                entry.Inputs?.Select(ToControlPlaneInputItem).ToArray());

    private static ControlPlanePendingInteractiveRequest ToControlPlanePendingInteractiveRequest(PendingInteractiveRequestReplay request)
            => new()
            {
                RequestId = request.RequestId,
                RequestIdRaw = request.RequestIdRaw,
                RequestKind = request.RequestKind,
                RequestMethod = request.RequestMethod,
                CallId = request.CallId,
                ThreadId = request.ThreadId,
                TurnId = request.TurnId,
                ToolName = request.ToolName,
                ServerName = request.ServerName,
                Text = request.Text,
                Status = request.Status,
                Phase = request.Phase,
                RequiresApproval = request.RequiresApproval,
                ApprovalKind = request.ApprovalKind,
                AvailableDecisions = request.AvailableDecisions,
                AvailableDecisionOptions = request.AvailableDecisionOptions?.Select(
                    static option => new ControlPlaneApprovalDecisionOption(
                        option.Type,
                        option.ExecPolicyAmendment is null ? null : new ControlPlaneExecPolicyAmendment(option.ExecPolicyAmendment.CommandPrefix),
                        option.NetworkPolicyAmendment is null ? null : new ControlPlaneNetworkPolicyAmendment(option.NetworkPolicyAmendment.Host, option.NetworkPolicyAmendment.Action)))
                    .ToArray(),
            };

        public Task<ControlPlaneLoadedThreadListResult> ListLoadedThreadsAsync(ControlPlaneLoadedThreadListQuery query, CancellationToken cancellationToken)
        {
            ThreadLoadedListRequests.Add(query);
            return Task.FromResult(ListLoadedThreadsHandler?.Invoke(query) ?? new ControlPlaneLoadedThreadListResult());
        }

        public Task<ControlPlaneThreadOperationResult> ReadThreadAsync(ControlPlaneReadThreadQuery query, CancellationToken cancellationToken)
        {
            ThreadReadRequests.Add(query);
            return Task.FromResult(ReadThreadHandler?.Invoke(query) ?? new ControlPlaneThreadOperationResult());
        }

        public Task<ControlPlaneThreadCommandAcceptedResult> CompactThreadAsync(ControlPlaneCompactThreadCommand command, CancellationToken cancellationToken)
        {
            CompactThreadRequests.Add((command.ThreadId.Value, command.KeepRecentTurns));
            return Task.FromResult(CompactThreadHandler?.Invoke(command.ThreadId.Value, command.KeepRecentTurns) ?? new ControlPlaneThreadCommandAcceptedResult());
        }

        public Task<ControlPlaneThreadCommandAcceptedResult> CleanBackgroundTerminalsAsync(ControlPlaneCleanBackgroundTerminalsCommand command, CancellationToken cancellationToken)
        {
            CleanBackgroundTerminalsRequests.Add(command.ThreadId.Value);
            return Task.FromResult(CleanBackgroundTerminalsHandler?.Invoke(command.ThreadId.Value) ?? new ControlPlaneThreadCommandAcceptedResult());
        }

        public Task<ControlPlaneThreadUnsubscribeResult> UnsubscribeThreadAsync(ControlPlaneUnsubscribeThreadCommand command, CancellationToken cancellationToken)
        {
            UnsubscribeThreadRequests.Add(command.ThreadId.Value);
            return Task.FromResult(UnsubscribeThreadHandler?.Invoke(command.ThreadId.Value) ?? new ControlPlaneThreadUnsubscribeResult());
        }

        public Task<ControlPlaneThreadElicitationResult> IncrementThreadElicitationAsync(ControlPlaneIncrementThreadElicitationCommand command, CancellationToken cancellationToken)
        {
            IncrementThreadElicitationRequests.Add(command.ThreadId.Value);
            return Task.FromResult(IncrementThreadElicitationHandler?.Invoke(command.ThreadId.Value) ?? new ControlPlaneThreadElicitationResult());
        }

        public Task<ControlPlaneThreadElicitationResult> DecrementThreadElicitationAsync(ControlPlaneDecrementThreadElicitationCommand command, CancellationToken cancellationToken)
        {
            DecrementThreadElicitationRequests.Add(command.ThreadId.Value);
            return Task.FromResult(DecrementThreadElicitationHandler?.Invoke(command.ThreadId.Value) ?? new ControlPlaneThreadElicitationResult());
        }

        public Task<ControlPlaneThreadOperationResult> UnarchiveThreadAsync(ControlPlaneUnarchiveThreadCommand command, CancellationToken cancellationToken)
        {
            UnarchiveThreadRequests.Add(command.ThreadId.Value);
            return Task.FromResult(UnarchiveThreadHandler?.Invoke(command.ThreadId.Value) ?? new ControlPlaneThreadOperationResult());
        }

        public Task<ControlPlaneThreadOperationResult> UpdateThreadMetadataAsync(ControlPlaneUpdateThreadMetadataCommand command, CancellationToken cancellationToken)
        {
            ThreadMetadataUpdateRequests.Add(command);
            return Task.FromResult(UpdateThreadMetadataHandler?.Invoke(command) ?? new ControlPlaneThreadOperationResult());
        }

        public Task<ControlPlaneThreadOperationResult> RollbackThreadAsync(ControlPlaneRollbackThreadCommand command, CancellationToken cancellationToken)
        {
            ThreadRollbackRequests.Add(command);
            return Task.FromResult(RollbackThreadHandler?.Invoke(command) ?? new ControlPlaneThreadOperationResult());
        }

        public Task<ControlPlaneRealtimeCommandAcceptedResult> StartRealtimeAsync(ControlPlaneRealtimeStartCommand command, CancellationToken cancellationToken)
        {
            RealtimeStartRequests.Add(command);
            return Task.FromResult(StartRealtimeHandler?.Invoke(command) ?? new ControlPlaneRealtimeCommandAcceptedResult());
        }

        public Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeTextAsync(ControlPlaneRealtimeAppendTextCommand command, CancellationToken cancellationToken)
        {
            RealtimeAppendTextRequests.Add(command);
            return Task.FromResult(AppendRealtimeTextHandler?.Invoke(command) ?? new ControlPlaneRealtimeCommandAcceptedResult());
        }

        public Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeAudioAsync(ControlPlaneRealtimeAppendAudioCommand command, CancellationToken cancellationToken)
        {
            RealtimeAppendAudioRequests.Add(command);
            return Task.FromResult(AppendRealtimeAudioHandler?.Invoke(command) ?? new ControlPlaneRealtimeCommandAcceptedResult());
        }

        public Task<ControlPlaneRealtimeCommandAcceptedResult> HandoffRealtimeOutputAsync(ControlPlaneRealtimeHandoffOutputCommand command, CancellationToken cancellationToken)
        {
            RealtimeHandoffOutputRequests.Add(command);
            return Task.FromResult(HandoffRealtimeOutputHandler?.Invoke(command) ?? new ControlPlaneRealtimeCommandAcceptedResult());
        }

        public Task<ControlPlaneRealtimeCommandAcceptedResult> StopRealtimeAsync(ControlPlaneRealtimeStopCommand command, CancellationToken cancellationToken)
        {
            RealtimeStopRequests.Add(command);
            return Task.FromResult(StopRealtimeHandler?.Invoke(command) ?? new ControlPlaneRealtimeCommandAcceptedResult());
        }

        public Task<ControlPlaneFuzzyFileSearchResult> SearchFuzzyFilesAsync(ControlPlaneFuzzyFileSearchQuery request, CancellationToken cancellationToken)
        {
            FuzzySearchRequests.Add(request);
            return Task.FromResult(SearchFuzzyFilesHandler?.Invoke(request) ?? new ControlPlaneFuzzyFileSearchResult());
        }

        public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StartFuzzyFileSearchSessionAsync(ControlPlaneStartFuzzyFileSearchSessionCommand request, CancellationToken cancellationToken)
        {
            FuzzySessionStartRequests.Add(request);
            return Task.FromResult(StartFuzzyFileSearchSessionHandler?.Invoke(request) ?? new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
        }

        public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> UpdateFuzzyFileSearchSessionAsync(ControlPlaneUpdateFuzzyFileSearchSessionCommand request, CancellationToken cancellationToken)
        {
            FuzzySessionUpdateRequests.Add(request);
            return Task.FromResult(UpdateFuzzyFileSearchSessionHandler?.Invoke(request) ?? new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
        }

        public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StopFuzzyFileSearchSessionAsync(ControlPlaneStopFuzzyFileSearchSessionCommand request, CancellationToken cancellationToken)
        {
            FuzzySessionStopRequests.Add(request);
            return Task.FromResult(StopFuzzyFileSearchSessionHandler?.Invoke(request) ?? new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
        }

        public Task<ControlPlaneConfigSnapshotResult> ReadConfigAsync(ControlPlaneConfigReadQuery request, CancellationToken cancellationToken)
        {
            ConfigReadRequests.Add(request);
            return Task.FromResult(ReadConfigHandler?.Invoke(request) ?? new ControlPlaneConfigSnapshotResult());
        }

        public Task<ControlPlaneConfigRequirementsResult> ReadConfigRequirementsAsync(ControlPlaneConfigRequirementsQuery request, CancellationToken cancellationToken)
        {
            ConfigRequirementsReadRequests.Add(request);
            return Task.FromResult(ReadConfigRequirementsHandler?.Invoke(request) ?? new ControlPlaneConfigRequirementsResult());
        }

        public Task<ControlPlaneModelCatalogResult> ListModelsAsync(ControlPlaneModelCatalogQuery request, CancellationToken cancellationToken)
        {
            ModelListRequests.Add(request);
            return Task.FromResult(ListModelsHandler?.Invoke(request) ?? new ControlPlaneModelCatalogResult());
        }

        public Task<CapabilityCatalogSnapshot> GetCapabilityCatalogAsync(
            GetCapabilityCatalog request,
            CancellationToken cancellationToken)
        {
            ProviderCatalogRequests.Add(request);
            return Task.FromResult(ListProviderProfilesHandler?.Invoke(request) ?? new CapabilityCatalogSnapshot());
        }

        public Task<ResolvedEngineBinding> ResolveEngineBindingAsync(
            ResolveEngineBinding request,
            CancellationToken cancellationToken)
        {
            EngineBindingRequests.Add(request);
            return Task.FromResult(ResolveEngineBindingHandler?.Invoke(request) ?? new ResolvedEngineBinding(null));
        }

        public Task<ControlPlaneAppCatalogResult> ListAppsAsync(ControlPlaneAppCatalogQuery request, CancellationToken cancellationToken)
        {
            AppListRequests.Add(request);
            return Task.FromResult(ListAppsHandler?.Invoke(request) ?? new ControlPlaneAppCatalogResult());
        }

        public Task<ControlPlaneConfigWriteResult> WriteConfigValueAsync(ControlPlaneConfigValueWriteCommand request, CancellationToken cancellationToken)
        {
            ConfigValueWriteRequests.Add(request);
            return Task.FromResult(WriteConfigValueHandler?.Invoke(request) ?? new ControlPlaneConfigWriteResult());
        }

        public Task<ControlPlaneConfigWriteResult> WriteConfigBatchAsync(ControlPlaneConfigBatchWriteCommand request, CancellationToken cancellationToken)
        {
            ConfigBatchWriteRequests.Add(request);
            return Task.FromResult(WriteConfigBatchHandler?.Invoke(request) ?? new ControlPlaneConfigWriteResult());
        }

        public Task<CollaborationSpace> CreateSpaceAsync(CreateCollaborationSpace command, CancellationToken cancellationToken)
        {
            CollaborationSpaceCreateRequests.Add(command);
            return Task.FromResult(
                CreateCollaborationSpaceHandler?.Invoke(command)
                ?? new CollaborationSpace(command.SpaceId, command.Key, command.DisplayName, command.Profile, command.Defaults, command.PolicyRef));
        }

        public Task<CollaborationSpace> ConfigureSpaceAsync(ConfigureCollaborationSpace command, CancellationToken cancellationToken)
        {
            CollaborationSpaceConfigureRequests.Add(command);
            return Task.FromResult(
                ConfigureCollaborationSpaceHandler?.Invoke(command)
                ?? new CollaborationSpace(
                    command.SpaceId,
                    command.SpaceId.Value,
                    command.DisplayName ?? command.SpaceId.Value,
                    command.Profile ?? new CollaborationSpaceProfile("fake collaboration"),
                    command.Defaults ?? CollaborationDefaultSet.Empty,
                    command.PolicyRef));
        }

        public Task<bool> ArchiveSpaceAsync(ArchiveCollaborationSpace command, CancellationToken cancellationToken)
        {
            CollaborationSpaceArchiveRequests.Add(command);
            return Task.FromResult(ArchiveCollaborationSpaceHandler?.Invoke(command) ?? true);
        }

        public Task<CollaborationSpaceOverviewProjection?> GetSpaceOverviewAsync(GetCollaborationSpaceOverview query, CancellationToken cancellationToken)
        {
            CollaborationSpaceOverviewRequests.Add(query);
            return Task.FromResult(GetCollaborationSpaceOverviewHandler?.Invoke(query));
        }

        public Task<TianShu.Contracts.Projections.CollaborationSpaceProjection?> GetSpaceProjectionAsync(GetCollaborationSpaceProjection query, CancellationToken cancellationToken)
        {
            CollaborationSpaceProjectionRequests.Add(query);
            return Task.FromResult<TianShu.Contracts.Projections.CollaborationSpaceProjection?>(
                GetCollaborationSpaceProjectionHandler?.Invoke(query)
                ?? new TianShu.Contracts.Projections.CollaborationSpaceProjection(
                    new CollaborationSpaceRef(query.SpaceId, query.SpaceId.Value, query.SpaceId.Value),
                    ActiveSessionCount: 0,
                    ActiveThreadCount: 0,
                    IsArchived: false));
        }

        public Task<IReadOnlyList<CollaborationSpaceOverviewProjection>> ListSpacesAsync(ListCollaborationSpaces query, CancellationToken cancellationToken)
        {
            CollaborationSpaceListRequests.Add(query);
            return Task.FromResult(ListCollaborationSpacesHandler?.Invoke(query) ?? Array.Empty<CollaborationSpaceOverviewProjection>());
        }

        public Task<bool> BindParticipantToSessionAsync(BindParticipantToSession command, CancellationToken cancellationToken)
        {
            ParticipantSessionBindRequests.Add(command);
            return Task.FromResult(BindParticipantToSessionHandler?.Invoke(command) ?? true);
        }

        public Task<bool> BindParticipantToWorkflowAsync(BindParticipantToWorkflow command, CancellationToken cancellationToken)
        {
            ParticipantWorkflowBindRequests.Add(command);
            return Task.FromResult(BindParticipantToWorkflowHandler?.Invoke(command) ?? true);
        }

        public Task<bool> UpdateParticipantRoleAsync(UpdateParticipantRole command, CancellationToken cancellationToken)
        {
            ParticipantRoleUpdateRequests.Add(command);
            return Task.FromResult(UpdateParticipantRoleHandler?.Invoke(command) ?? true);
        }

        public Task<ParticipantProjection?> GetParticipantProjectionAsync(GetParticipantProjection query, CancellationToken cancellationToken)
        {
            ParticipantProjectionRequests.Add(query);
            return Task.FromResult<ParticipantProjection?>(
                GetParticipantProjectionHandler?.Invoke(query)
                ?? new ParticipantProjection(query.ParticipantId, ParticipantKind.Agent, query.ParticipantId.Value, "member"));
        }

        public Task<TianShu.Contracts.Projections.ParticipantProjection?> GetParticipantViewProjectionAsync(GetParticipantViewProjection query, CancellationToken cancellationToken)
        {
            ParticipantViewProjectionRequests.Add(query);
            return Task.FromResult<TianShu.Contracts.Projections.ParticipantProjection?>(
                GetParticipantViewProjectionHandler?.Invoke(query)
                ?? new TianShu.Contracts.Projections.ParticipantProjection(
                    new ParticipantRef(query.ParticipantId, ParticipantKind.Agent, query.ParticipantId.Value),
                    ScopeKind: "participant",
                    ScopeKey: query.ParticipantId.Value,
                    Role: "member",
                    IsActive: true));
        }

        public Task<IReadOnlyList<ParticipantProjection>> ListParticipantsInScopeAsync(ListParticipantsInScope query, CancellationToken cancellationToken)
        {
            ParticipantListRequests.Add(query);
            return Task.FromResult(ListParticipantsInScopeHandler?.Invoke(query) ?? Array.Empty<ParticipantProjection>());
        }

        public Task<SessionOverviewProjection?> GetSessionOverviewAsync(GetSessionOverview query, CancellationToken cancellationToken)
        {
            SessionOverviewRequests.Add(query);
            return Task.FromResult(GetSessionOverviewHandler?.Invoke(query));
        }

        public Task<IReadOnlyList<SessionOverviewProjection>> ListSessionsAsync(ListSessions query, CancellationToken cancellationToken)
        {
            SessionListRequests.Add(query);
            return Task.FromResult(ListSessionsHandler?.Invoke(query) ?? Array.Empty<SessionOverviewProjection>());
        }

        public Task<ExecutionTrace?> GetExecutionTraceAsync(GetExecutionTrace query, CancellationToken cancellationToken)
        {
            ExecutionTraceRequests.Add(query);
            return Task.FromResult(GetExecutionTraceHandler?.Invoke(query));
        }

        public Task<IReadOnlyList<AttemptSummary>> ListAttemptSummariesAsync(ListAttemptSummaries query, CancellationToken cancellationToken)
        {
            AttemptSummaryListRequests.Add(query);
            return Task.FromResult(ListAttemptSummariesHandler?.Invoke(query) ?? Array.Empty<AttemptSummary>());
        }

        public Task<Account?> GetAccountProfileAsync(GetAccountProfile query, CancellationToken cancellationToken)
        {
            AccountProfileRequests.Add(query);
            return Task.FromResult(GetAccountProfileHandler?.Invoke(query));
        }

        public Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(ListBoundDevices query, CancellationToken cancellationToken)
        {
            BoundDeviceListRequests.Add(query);
            return Task.FromResult(ListBoundDevicesHandler?.Invoke(query) ?? Array.Empty<DeviceBinding>());
        }

        public Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(ListMemoryProviders query, CancellationToken cancellationToken)
        {
            MemoryProviderListRequests.Add(query);
            return Task.FromResult(ListMemoryProvidersHandler?.Invoke(query) ?? Array.Empty<MemoryProviderDescriptor>());
        }

        public Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(ListMemorySpaces query, CancellationToken cancellationToken)
        {
            MemorySpaceListRequests.Add(query);
            return Task.FromResult(ListMemorySpacesHandler?.Invoke(query) ?? Array.Empty<MemorySpace>());
        }

        public Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay query, CancellationToken cancellationToken)
        {
            MemoryOverlayRequests.Add(query);
            return Task.FromResult(ResolveMemoryOverlayHandler?.Invoke(query) ?? new MemoryOverlay());
        }

        public Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory query, CancellationToken cancellationToken)
        {
            MemoryFilterRequests.Add(query);
            return Task.FromResult(FilterMemoryHandler?.Invoke(query) ?? new MemoryQueryResult());
        }

        public Task<MemoryReviewQueryResult> ListMemoryReviewsAsync(ListMemoryReviews query, CancellationToken cancellationToken)
        {
            MemoryReviewListRequests.Add(query);
            return Task.FromResult(ListMemoryReviewsHandler?.Invoke(query) ?? new MemoryReviewQueryResult());
        }

        public Task<MemoryMutationResult> AddMemoryAsync(AddMemory command, CancellationToken cancellationToken)
        {
            MemoryAddRequests.Add(command);
            return Task.FromResult(AddMemoryHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(ExtractMemory command, CancellationToken cancellationToken)
        {
            MemoryExtractRequests.Add(command);
            return Task.FromResult(ExtractMemoryHandler?.Invoke(command) ?? Array.Empty<MemoryCandidate>());
        }

        public Task<MemoryMutationResult> ImportMemoryAsync(ImportMemory command, CancellationToken cancellationToken)
        {
            MemoryImportRequests.Add(command);
            return Task.FromResult(ImportMemoryHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<MemoryQueryResult> ExportMemoryAsync(ExportMemory command, CancellationToken cancellationToken)
        {
            MemoryExportRequests.Add(command);
            return Task.FromResult(ExportMemoryHandler?.Invoke(command) ?? new MemoryQueryResult());
        }

        public Task<MemoryMutationResult> BindMemoryProviderAsync(BindMemoryProvider command, CancellationToken cancellationToken)
        {
            MemoryBindProviderRequests.Add(command);
            return Task.FromResult(BindMemoryProviderHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(RunMemoryConsolidation command, CancellationToken cancellationToken)
        {
            MemoryConsolidationRequests.Add(command);
            return Task.FromResult(RunMemoryConsolidationHandler?.Invoke(command) ?? new MemoryConsolidationRunResult(0, 0));
        }

        public Task<MemoryMutationResult> ForgetMemoryAsync(ForgetMemory command, CancellationToken cancellationToken)
        {
            MemoryForgetRequests.Add(command);
            return Task.FromResult(ForgetMemoryHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<MemoryMutationResult> DeleteMemoryAsync(DeleteMemory command, CancellationToken cancellationToken)
        {
            MemoryDeleteRequests.Add(command);
            return Task.FromResult(DeleteMemoryHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<MemoryMutationResult> SupersedeMemoryAsync(SupersedeMemory command, CancellationToken cancellationToken)
        {
            MemorySupersedeRequests.Add(command);
            return Task.FromResult(SupersedeMemoryHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<MemoryMutationResult> ApproveMemoryReviewAsync(ApproveMemoryReview command, CancellationToken cancellationToken)
        {
            MemoryApproveReviewRequests.Add(command);
            return Task.FromResult(ApproveMemoryReviewHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<MemoryMutationResult> DemoteMemoryReviewAsync(DemoteMemoryReview command, CancellationToken cancellationToken)
        {
            MemoryDemoteReviewRequests.Add(command);
            return Task.FromResult(DemoteMemoryReviewHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<MemoryMutationResult> MergeMemoryReviewAsync(MergeMemoryReview command, CancellationToken cancellationToken)
        {
            MemoryMergeReviewRequests.Add(command);
            return Task.FromResult(MergeMemoryReviewHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<MemoryMutationResult> RestoreMemoryReviewAsync(RestoreMemoryReview command, CancellationToken cancellationToken)
        {
            MemoryRestoreReviewRequests.Add(command);
            return Task.FromResult(RestoreMemoryReviewHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken)
        {
            MemoryFeedbackRequests.Add(command);
            return Task.FromResult(RecordMemoryFeedbackHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<MemoryMutationResult> RecordMemoryCitationAsync(RecordMemoryCitation command, CancellationToken cancellationToken)
        {
            MemoryCitationRequests.Add(command);
            return Task.FromResult(RecordMemoryCitationHandler?.Invoke(command) ?? new MemoryMutationResult(true));
        }

        public Task<ControlPlaneSkillCatalogResult> ListSkillsAsync(ControlPlaneSkillCatalogQuery request, CancellationToken cancellationToken)
        {
            SkillsListRequests.Add(request);
            return Task.FromResult(ListSkillsHandler?.Invoke(request) ?? new ControlPlaneSkillCatalogResult());
        }

        public Task<ControlPlaneSkillConfigWriteResult> WriteSkillsConfigAsync(ControlPlaneSkillConfigWriteCommand request, CancellationToken cancellationToken)
        {
            SkillsConfigWriteRequests.Add(request);
            return Task.FromResult(WriteSkillsConfigHandler?.Invoke(request) ?? new ControlPlaneSkillConfigWriteResult());
        }

        public Task<ControlPlaneRemoteSkillCatalogResult> ListRemoteSkillsAsync(ControlPlaneRemoteSkillCatalogQuery request, CancellationToken cancellationToken)
        {
            SkillsRemoteListRequests.Add(request);
            return Task.FromResult(ListRemoteSkillsHandler?.Invoke(request) ?? new ControlPlaneRemoteSkillCatalogResult());
        }

        public Task<ControlPlaneRemoteSkillExportResult> ExportRemoteSkillAsync(ControlPlaneRemoteSkillExportCommand request, CancellationToken cancellationToken)
        {
            SkillsRemoteExportRequests.Add(request);
            return Task.FromResult(ExportRemoteSkillHandler?.Invoke(request) ?? new ControlPlaneRemoteSkillExportResult());
        }

        public Task<ControlPlanePluginCatalogResult> ListPluginsAsync(ControlPlanePluginCatalogQuery request, CancellationToken cancellationToken)
        {
            PluginListRequests.Add(request);
            return Task.FromResult(ListPluginsHandler?.Invoke(request) ?? new ControlPlanePluginCatalogResult());
        }

        public Task<ControlPlanePluginReadResult> ReadPluginAsync(ControlPlanePluginReadQuery request, CancellationToken cancellationToken)
        {
            PluginReadRequests.Add(request);
            return Task.FromResult(ReadPluginHandler?.Invoke(request) ?? new ControlPlanePluginReadResult());
        }

        public Task<ControlPlanePluginInstallResult> InstallPluginAsync(ControlPlanePluginInstallCommand request, CancellationToken cancellationToken)
        {
            PluginInstallRequests.Add(request);
            return Task.FromResult(InstallPluginHandler?.Invoke(request) ?? new ControlPlanePluginInstallResult());
        }

        public Task<ControlPlanePluginUninstallResult> UninstallPluginAsync(ControlPlanePluginUninstallCommand request, CancellationToken cancellationToken)
        {
            PluginUninstallRequests.Add(request);
            return Task.FromResult(UninstallPluginHandler?.Invoke(request) ?? new ControlPlanePluginUninstallResult());
        }

        public Task<ControlPlaneReviewStartResult> StartReviewAsync(ControlPlaneReviewStartCommand request, CancellationToken cancellationToken)
        {
            ReviewStartRequests.Add(request);
            return Task.FromResult(StartReviewHandler?.Invoke(request) ?? new ControlPlaneReviewStartResult());
        }

        public Task<ControlPlaneExperimentalFeatureCatalogResult> ListExperimentalFeaturesAsync(ControlPlaneExperimentalFeatureQuery request, CancellationToken cancellationToken)
        {
            ExperimentalFeatureListRequests.Add(request);
            return Task.FromResult(ListExperimentalFeaturesHandler?.Invoke(request) ?? new ControlPlaneExperimentalFeatureCatalogResult());
        }

        public Task<ControlPlaneCollaborationModeCatalogResult> ListCollaborationModesAsync(CancellationToken cancellationToken)
            => Task.FromResult(ListCollaborationModesHandler?.Invoke() ?? new ControlPlaneCollaborationModeCatalogResult());

        public Task<ControlPlaneMcpServerCatalogResult> ListMcpServerStatusAsync(ControlPlaneMcpServerStatusQuery request, CancellationToken cancellationToken)
        {
            McpServerStatusListRequests.Add(request);
            return Task.FromResult(ListMcpServerStatusHandler?.Invoke(request) ?? new ControlPlaneMcpServerCatalogResult());
        }

        public Task<ControlPlaneMcpServerReloadResult> ReloadMcpServersAsync(ControlPlaneMcpServerReloadCommand request, CancellationToken cancellationToken)
            => Task.FromResult(ReloadMcpServersHandler?.Invoke() ?? new ControlPlaneMcpServerReloadResult());

        public Task<ControlPlaneProviderPackageReloadResult> ReloadProviderPackagesAsync(ControlPlaneProviderPackageReloadCommand request, CancellationToken cancellationToken)
            => Task.FromResult(ReloadProviderPackagesHandler?.Invoke() ?? new ControlPlaneProviderPackageReloadResult());

        public Task<ControlPlaneMcpServerOauthLoginStartResult> StartMcpServerOauthLoginAsync(ControlPlaneMcpServerOauthLoginStartCommand request, CancellationToken cancellationToken)
        {
            McpServerOauthLoginRequests.Add(request);
            if (StartMcpServerOauthLoginHandler is not null)
            {
                return Task.FromResult(StartMcpServerOauthLoginHandler.Invoke(request));
            }

            var payload = new Dictionary<string, object?>
            {
                ["name"] = request.Name,
            };
            if (request.TimeoutSecs.HasValue)
            {
                payload["timeoutSecs"] = request.TimeoutSecs.Value;
            }

            var result = InvokeViaHandler("mcpServer/oauth/login", payload).GetAwaiter().GetResult();
            return Task.FromResult(new ControlPlaneMcpServerOauthLoginStartResult
            {
                AuthorizationUrl = result.TryGetProperty("authorizationUrl", out var authorizationUrl) && authorizationUrl is not null
                    ? authorizationUrl.GetString()
                    : null,
            });
        }

        public Task<ControlPlaneConversationArtifact?> GetConversationSummaryAsync(ControlPlaneConversationArtifactQuery request, CancellationToken cancellationToken)
        {
            ConversationSummaryRequests.Add(request);
            return Task.FromResult(GetConversationSummaryHandler?.Invoke(request));
        }

        public Task<ControlPlaneGitDiffArtifact> GetGitDiffToRemoteAsync(ControlPlaneGitDiffArtifactQuery request, CancellationToken cancellationToken)
        {
            GitDiffToRemoteRequests.Add(request);
            return Task.FromResult(GetGitDiffToRemoteHandler?.Invoke(request) ?? new ControlPlaneGitDiffArtifact());
        }

        public void EmitRawNotification(string rawNotificationJson)
        {
            using var document = JsonDocument.Parse(rawNotificationJson);
            var method = document.RootElement.GetProperty("method").GetString() ?? "notification";
            var parameters = document.RootElement.TryGetProperty("params", out var value) ? value : default;
            TianShuCliIntegrationTests.EmitProjectedStreamEvent(this, new AgentStreamEvent
            {
                Kind = AgentStreamEventKind.Info,
                Message = method,
                WindowsSandboxSetup = BuildWindowsSandboxPayload(method, parameters),
                RealtimeSession = BuildRealtimeSessionPayload(method, parameters),
                McpServerOauthLogin = BuildMcpServerOauthLoginPayload(method, parameters),
                FuzzyFileSearchSession = BuildFuzzyFileSearchSessionPayload(method, parameters),
                RawJson = rawNotificationJson,
            });
        }

        private static WindowsSandboxSetupPayload? BuildWindowsSandboxPayload(string method, JsonElement parameters)
        {
            if (!string.Equals(method, "windowsSandbox/setupCompleted", StringComparison.Ordinal))
            {
                return null;
            }

            return new WindowsSandboxSetupPayload(
                ReadString(parameters, "mode"),
                ReadBool(parameters, "success"),
                ReadString(parameters, "error"));
        }

        private static RealtimeSessionPayload? BuildRealtimeSessionPayload(string method, JsonElement parameters)
        {
            if (!string.Equals(method, "thread/realtime/started", StringComparison.Ordinal))
            {
                return null;
            }

            return new RealtimeSessionPayload(
                ReadString(parameters, "threadId"),
                ReadString(parameters, "sessionId"));
        }

        private static McpServerOauthLoginPayload? BuildMcpServerOauthLoginPayload(string method, JsonElement parameters)
        {
            if (!string.Equals(method, "mcpServer/oauthLogin/completed", StringComparison.Ordinal))
            {
                return null;
            }

            return new McpServerOauthLoginPayload(
                ReadString(parameters, "name"),
                ReadBool(parameters, "success"),
                ReadString(parameters, "error"));
        }

        private static FuzzyFileSearchSessionPayload? BuildFuzzyFileSearchSessionPayload(string method, JsonElement parameters)
        {
            if (!string.Equals(method, "fuzzyFileSearch/sessionUpdated", StringComparison.Ordinal)
                && !string.Equals(method, "fuzzyFileSearch/sessionCompleted", StringComparison.Ordinal))
            {
                return null;
            }

            var files = Array.Empty<FuzzyFileSearchFilePayload>();
            if (string.Equals(method, "fuzzyFileSearch/sessionUpdated", StringComparison.Ordinal)
                && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("files", out var fileArray)
                && fileArray.ValueKind == JsonValueKind.Array)
            {
                files = ReadFuzzyFiles(fileArray);
            }

            return new FuzzyFileSearchSessionPayload(
                ReadString(parameters, "sessionId"),
                files,
                IsCompleted: string.Equals(method, "fuzzyFileSearch/sessionCompleted", StringComparison.Ordinal));
        }

        private static FuzzyFileSearchFilePayload[] ReadFuzzyFiles(JsonElement fileArray)
        {
            var files = new List<FuzzyFileSearchFilePayload>();
            foreach (var item in fileArray.EnumerateArray())
            {
                switch (item.ValueKind)
                {
                    case JsonValueKind.String:
                        files.Add(new FuzzyFileSearchFilePayload(item.GetString(), null));
                        break;
                    case JsonValueKind.Object:
                        files.Add(new FuzzyFileSearchFilePayload(
                            ReadString(item, "path"),
                            ReadString(item, "fileName")));
                        break;
                }
            }

            return [.. files];
        }

        private static string? ReadString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null => null,
                _ => value.ToString(),
            };
        }

        private static bool? ReadBool(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }
        public Task<ControlPlaneCommandExecutionResult> StartCommandExecutionAsync(ControlPlaneCommandExecutionStartCommand request, CancellationToken cancellationToken)
        {
            CommandExecutionStartRequests.Add(request);
            return Task.FromResult(StartCommandExecutionHandler?.Invoke(request) ?? new ControlPlaneCommandExecutionResult());
        }

        public Task<ControlPlaneCommandExecutionCommandAcceptedResult> WriteCommandExecutionAsync(ControlPlaneCommandExecutionWriteCommand request, CancellationToken cancellationToken)
        {
            CommandExecutionWriteRequests.Add(request);
            return Task.FromResult(WriteCommandExecutionHandler?.Invoke(request) ?? new ControlPlaneCommandExecutionCommandAcceptedResult());
        }

        public Task<ControlPlaneCommandExecutionCommandAcceptedResult> TerminateCommandExecutionAsync(ControlPlaneCommandExecutionTerminateCommand request, CancellationToken cancellationToken)
        {
            CommandExecutionTerminateRequests.Add(request);
            return Task.FromResult(TerminateCommandExecutionHandler?.Invoke(request) ?? new ControlPlaneCommandExecutionCommandAcceptedResult());
        }

        public Task<ControlPlaneCommandExecutionCommandAcceptedResult> ResizeCommandExecutionAsync(ControlPlaneCommandExecutionResizeCommand request, CancellationToken cancellationToken)
        {
            CommandExecutionResizeRequests.Add(request);
            return Task.FromResult(ResizeCommandExecutionHandler?.Invoke(request) ?? new ControlPlaneCommandExecutionCommandAcceptedResult());
        }

        public Task<ControlPlaneCodeModeResult> ExecuteCodeModeAsync(ControlPlaneCodeModeExecCommand request, CancellationToken cancellationToken)
            => InvokeCodeModeViaHandler(
                "exec",
                new Dictionary<string, object?>
                {
                    ["threadId"] = request.ThreadId.Value,
                    ["input"] = request.Input,
                    ["yieldTimeMs"] = request.YieldTimeMs,
                    ["maxOutputTokens"] = request.MaxOutputTokens,
                });

        public Task<ControlPlaneCodeModeResult> WaitCodeModeAsync(ControlPlaneCodeModeWaitCommand request, CancellationToken cancellationToken)
            => InvokeCodeModeViaHandler(
                "exec_wait",
                new Dictionary<string, object?>
                {
                    ["threadId"] = request.ThreadId.Value,
                    ["cellId"] = request.CellId,
                    ["yieldTimeMs"] = request.YieldTimeMs,
                    ["maxTokens"] = request.MaxTokens,
                    ["terminate"] = request.Terminate,
                });

        public Task<ControlPlaneAgentThreadRegistrationResult> RegisterAgentThreadAsync(ControlPlaneRegisterAgentThreadCommand request, CancellationToken cancellationToken)
        {
            AgentThreadRegistrationCommands.Add(request);
            return Task.FromResult(RegisterAgentThreadHandler?.Invoke(request) ?? new ControlPlaneAgentThreadRegistrationResult());
        }

        public Task<Workflow> CreateWorkflowAsync(CreateWorkflow request, CancellationToken cancellationToken)
        {
            WorkflowCreateRequests.Add(request);
            return Task.FromResult(
                CreateWorkflowHandler?.Invoke(request)
                ?? new Workflow(
                    request.WorkflowId,
                    new CollaborationSpaceRef(request.CollaborationSpaceId, request.CollaborationSpaceId.Value, request.CollaborationSpaceId.Value),
                    request.DisplayName,
                    WorkflowState.Draft,
                    request.OwnerParticipant,
                    request.ThreadId));
        }

        public Task<PlanProjection> PublishPlanAsync(PublishPlan request, CancellationToken cancellationToken)
        {
            WorkflowPublishPlanRequests.Add(request);
            return Task.FromResult(PublishPlanHandler?.Invoke(request) ?? new PlanProjection(request.WorkflowId, request.Plan));
        }

        public Task<TianShu.Contracts.Workflows.Task> CreateTaskAsync(CreateTask request, CancellationToken cancellationToken)
        {
            WorkflowCreateTaskRequests.Add(request);
            return Task.FromResult(CreateWorkflowTaskHandler?.Invoke(request) ?? request.Task);
        }

        public Task<TianShu.Contracts.Workflows.Task?> UpdateTaskStateAsync(UpdateTaskState request, CancellationToken cancellationToken)
        {
            WorkflowUpdateTaskStateRequests.Add(request);
            return Task.FromResult(UpdateWorkflowTaskStateHandler?.Invoke(request));
        }

        public Task<ControlPlaneJobOperationResult> CreateAgentJobAsync(ControlPlaneCreateJobCommand request, CancellationToken cancellationToken)
        {
            AgentJobCreateRequests.Add(request);
            return Task.FromResult(CreateAgentJobHandler?.Invoke(request) ?? new ControlPlaneJobOperationResult());
        }

        public Task<ControlPlaneJobOperationResult> DispatchAgentJobAsync(ControlPlaneDispatchJobCommand request, CancellationToken cancellationToken)
        {
            AgentJobDispatchRequests.Add(request);
            return Task.FromResult(DispatchAgentJobHandler?.Invoke(request) ?? new ControlPlaneJobOperationResult());
        }

        public Task<ControlPlaneJobOperationResult> ReportAgentJobItemAsync(ControlPlaneReportJobItemCommand request, CancellationToken cancellationToken)
        {
            AgentJobItemReportRequests.Add(request);
            return Task.FromResult(ReportAgentJobItemHandler?.Invoke(request) ?? new ControlPlaneJobOperationResult());
        }

        public Task<ControlPlaneJobOperationResult> ReadAgentJobAsync(ControlPlaneReadJobQuery request, CancellationToken cancellationToken)
        {
            AgentJobReadRequests.Add(request);
            return Task.FromResult(ReadAgentJobHandler?.Invoke(request) ?? new ControlPlaneJobOperationResult());
        }

        public Task<ControlPlaneJobListResult> ListAgentJobsAsync(ControlPlaneListJobsQuery request, CancellationToken cancellationToken)
            => Task.FromResult(ListAgentJobsHandler?.Invoke(request) ?? new ControlPlaneJobListResult());

        Task<ControlPlaneFeedbackUploadResult> IGovernanceControlPlaneClient.UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand request, CancellationToken cancellationToken)
        {
            FeedbackUploadRequests.Add(request);
            GovernanceFeedbackUploadRequests.Add(request);
            return Task.FromResult(UploadFeedbackHandler?.Invoke(request) ?? new ControlPlaneFeedbackUploadResult());
        }

        Task<ControlPlaneFeedbackUploadResult> IDiagnosticsControlPlaneClient.UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand request, CancellationToken cancellationToken)
        {
            FeedbackUploadRequests.Add(request);
            DiagnosticsFeedbackUploadRequests.Add(request);
            return Task.FromResult(UploadFeedbackHandler?.Invoke(request) ?? new ControlPlaneFeedbackUploadResult());
        }

        public Task<ControlPlaneDebugClearMemoriesResult> ClearDebugMemoriesAsync(CancellationToken cancellationToken)
        {
            ClearDebugMemoriesCallCount++;
            return Task.FromResult(ClearDebugMemoriesHandler?.Invoke() ?? new ControlPlaneDebugClearMemoriesResult
            {
                StateDbPath = "D:/TianShu/state/state.db",
                MemoryRootPath = "D:/TianShu/memories",
                RemovedMemoryRoot = true,
            });
        }

        public Task<ControlPlaneWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(ControlPlaneWindowsSandboxSetupStartCommand request, CancellationToken cancellationToken)
        {
            WindowsSandboxSetupRequests.Add(request);
            return Task.FromResult(StartWindowsSandboxSetupHandler?.Invoke(request) ?? new ControlPlaneWindowsSandboxSetupStartResult());
        }

        private Task<StructuredValue> InvokeViaHandler(string method, object? parameters)
        {
            var structuredParameters = parameters is null ? null : StructuredValue.FromPlainObject(parameters);
            Invocations.Add((method, structuredParameters));
            if (InvokeDiagnosticRpcHandler is null)
            {
                throw new InvalidOperationException("InvokeDiagnosticRpcHandler 未配置。");
            }

            return Task.FromResult(InvokeDiagnosticRpcHandler(method, structuredParameters));
        }

        private Task<ControlPlaneCodeModeResult> InvokeCodeModeViaHandler(string method, object? parameters)
        {
            var result = InvokeViaHandler(method, parameters).GetAwaiter().GetResult();
            return Task.FromResult(new ControlPlaneCodeModeResult
            {
                Success = result.TryGetProperty("success", out var successElement) && successElement is not null
                    ? successElement.GetBoolean()
                    : true,
                Status = result.TryGetProperty("status", out var statusElement) && statusElement is not null
                    ? statusElement.GetString() ?? string.Empty
                    : string.Empty,
                ThreadId = result.TryGetProperty("threadId", out var threadElement) && threadElement is not null
                    ? threadElement.GetString() is { Length: > 0 } threadId
                        ? new ThreadId(threadId)
                        : null
                    : null,
                TurnId = result.TryGetProperty("turnId", out var turnElement) && turnElement is not null
                    ? turnElement.GetString() is { Length: > 0 } turnId
                        ? new TurnId(turnId)
                        : null
                    : null,
                CellId = result.TryGetProperty("cellId", out var cellElement) && cellElement is not null
                    ? cellElement.GetString()
                    : null,
                Output = result.TryGetProperty("output", out var outputElement) && outputElement is not null
                    ? outputElement.GetString() ?? string.Empty
                    : string.Empty,
                ContentItems = result.TryGetProperty("contentItems", out var contentItemsElement) && contentItemsElement is not null
                    ? contentItemsElement.Items
                        .Select(static item => new ControlPlaneCodeModeOutputItem
                        {
                            Type = item.TryGetProperty("type", out var typeElement) && typeElement is not null
                                ? typeElement.GetString() ?? string.Empty
                                : string.Empty,
                            Text = item.TryGetProperty("text", out var textElement) && textElement is not null
                                ? textElement.GetString()
                                : null,
                            ImageUrl = item.TryGetProperty("imageUrl", out var imageUrlElement) && imageUrlElement is not null
                                ? imageUrlElement.GetString()
                                : null,
                            Detail = item.TryGetProperty("detail", out var detailElement) && detailElement is not null
                                ? detailElement.GetString()
                                : null,
                        })
                        .ToArray()
                    : Array.Empty<ControlPlaneCodeModeOutputItem>(),
            });
        }

        public Task<StructuredValue> InvokeDiagnosticRpcAsync(string method, StructuredValue? parameters, CancellationToken cancellationToken)
        {
            DiagnosticRpcCallCount++;
            return InvokeViaHandler(method, parameters);
        }


        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }
}
