using TianShu.AppHost.Configuration;
using TianShu.Cli.Interaction.Commands.Config;
using TianShu.Configuration;

namespace TianShu.Cli.Tests;

public sealed class InteractiveConfigCommandHandlerTests
{
    [Fact]
    public async Task HandleConfigCommandAsync_WhenSubcommandInvalid_WritesUsage()
    {
        var handler = new InteractiveConfigCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleConfigCommandAsync(
            new CliConsumerFakeRuntime(),
            new ChatCommandOptions(),
            "show",
            Context(writeLine: (text, isError) => output.Add((text, isError))),
            CancellationToken.None);

        Assert.Contains(output, static line => line.IsError && line.Text == "用法：/config 或 /config reload");
    }

    [Fact]
    public async Task HandleConfigCommandAsync_WhenNoSubcommand_LaunchesConfigGui()
    {
        var handler = new InteractiveConfigCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        string? launchedPath = null;

        await handler.HandleConfigCommandAsync(
            new CliConsumerFakeRuntime(),
            new ChatCommandOptions(),
            string.Empty,
            Context(
                resolveConfigGuiExecutable: static () => "D:/TianShu/TianShu.ConfigGui.exe",
                launchConfigGui: path =>
                {
                    launchedPath = path;
                    return true;
                },
                writeControlPlaneLine: (text, isError) => output.Add((text, isError))),
            CancellationToken.None);

        Assert.Equal("D:/TianShu/TianShu.ConfigGui.exe", launchedPath);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已启动 ConfigGUI", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleConfigCommandAsync_WhenGuiMissing_WritesError()
    {
        var handler = new InteractiveConfigCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleConfigCommandAsync(
            new CliConsumerFakeRuntime(),
            new ChatCommandOptions(),
            string.Empty,
            Context(
                resolveConfigGuiExecutable: static () => null,
                writeControlPlaneLine: (text, isError) => output.Add((text, isError))),
            CancellationToken.None);

        Assert.Contains(output, static line => line.IsError && line.Text.Contains("未找到 ConfigGUI", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleConfigReloadCommandAsync_WhenConversationRunning_RejectsReload()
    {
        var handler = new InteractiveConfigCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        var runtime = new CliConsumerFakeRuntime();

        await handler.HandleConfigReloadCommandAsync(
            runtime,
            new ChatCommandOptions(),
            string.Empty,
            Context(hasRunningConversation: () => true, writeLine: (text, isError) => output.Add((text, isError))),
            CancellationToken.None);

        Assert.Empty(runtime.ProviderPackageReloadCalls);
        Assert.Contains(output, static line => line.IsError && line.Text.Contains("当前回合仍在运行", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleConfigReloadCommandAsync_WhenNoCurrentThread_UsesRouteSetSummaryWithoutRuntimeOverride()
    {
        var handler = new InteractiveConfigCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        var options = new ChatCommandOptions();
        var runtime = new CliConsumerFakeRuntime();
        string? displayModel = null;

        await handler.HandleConfigReloadCommandAsync(
            runtime,
            options,
            string.Empty,
            Context(
                loadResolvedConfig: _ => CreateConfigWithRouteSet(
                    rootModel: "model_a",
                    rootProvider: "provider_a",
                    routeModel: "route_model",
                    routeProvider: "route_provider"),
                getCurrentThreadId: static () => null,
                setCurrentDisplayModel: model => displayModel = model,
                writeControlPlaneLine: (text, isError) => output.Add((text, isError))),
            CancellationToken.None);

        Assert.Null(options.RuntimeModel);
        Assert.Null(options.RuntimeModelProvider);
        Assert.Equal("route_model", displayModel);
        Assert.Single(runtime.ProviderPackageReloadCalls);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("model=route_model, provider=route_provider", StringComparison.Ordinal));
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("下一条消息会使用最新模型路由配置创建线程", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleConfigReloadCommandAsync_WhenCurrentThreadResumeSucceeds_UpdatesThreadSnapshotModel()
    {
        var runtime = new CliConsumerFakeRuntime
        {
            ResumeThreadRequestAsyncHandler = static (request, _) => Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = request.ThreadId.Value,
                Name = "thread",
                SessionConfiguration = new AgentThreadSessionConfiguration
                {
                    Model = "model_resumed",
                    ModelProvider = "provider_resumed",
                },
            }),
        };
        var handler = new InteractiveConfigCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        var options = new ChatCommandOptions { WorkingDirectory = "D:/repo" };
        string? activeThread = null;
        string? displayModel = null;
        var markedTerminalTurn = false;

        await handler.HandleConfigReloadCommandAsync(
            runtime,
            options,
            string.Empty,
            Context(
                loadResolvedConfig: _ => CreateConfigWithRouteSet(
                    rootModel: "model_config",
                    rootProvider: "provider_config",
                    routeModel: "route_model",
                    routeProvider: "route_provider"),
                getCurrentThreadId: static () => "thread_1",
                setSessionActiveThreadId: value => activeThread = value,
                setCurrentDisplayModel: model => displayModel = model,
                markTerminalTurn: () => markedTerminalTurn = true,
                writeControlPlaneLine: (text, isError) => output.Add((text, isError))),
            CancellationToken.None);

        var request = Assert.Single(runtime.ResumeThreadRequestCalls);
        Assert.Equal("thread_1", request.ThreadId.Value);
        Assert.Null(request.Model);
        Assert.Null(request.ModelProvider);
        Assert.Equal("model_resumed", options.RuntimeModel);
        Assert.Equal("provider_resumed", options.RuntimeModelProvider);
        Assert.Equal("thread_1", activeThread);
        Assert.Equal("model_resumed", displayModel);
        Assert.True(markedTerminalTurn);
        Assert.Single(runtime.ProviderPackageReloadCalls);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已刷新当前会话配置", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleConfigReloadCommandAsync_WhenCurrentThreadResumeFails_WritesError()
    {
        var runtime = new CliConsumerFakeRuntime
        {
            ResumeThreadRequestAsyncHandler = static (_, _) => Task.FromResult<AgentThreadResumeResult?>(null),
        };
        var handler = new InteractiveConfigCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleConfigReloadCommandAsync(
            runtime,
            new ChatCommandOptions(),
            string.Empty,
            Context(
                loadResolvedConfig: _ => CreateConfigWithRouteSet(
                    rootModel: "model_config",
                    rootProvider: "provider_config",
                    routeModel: "route_model",
                    routeProvider: "route_provider"),
                getCurrentThreadId: static () => "thread_1",
                writeControlPlaneLine: (text, isError) => output.Add((text, isError))),
            CancellationToken.None);

        Assert.Contains(output, static line => line.IsError && line.Text == "刷新当前会话配置失败。");
    }

    private static ResolvedTianShuConfig CreateConfigWithRouteSet(
        string rootModel,
        string rootProvider,
        string routeModel,
        string routeProvider)
        => new()
        {
            Model = rootModel,
            ModelProvider = rootProvider,
            RawConfig = new Dictionary<string, object?>
            {
                ["model_route_set"] = "default",
                ["model_route_sets"] = new Dictionary<string, object?>
                {
                    ["default"] = new Dictionary<string, object?>
                    {
                        ["routes"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["kind"] = "default",
                                ["candidates"] = new object?[]
                                {
                                    new Dictionary<string, object?>
                                    {
                                        ["provider"] = routeProvider,
                                        ["model"] = routeModel,
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

    private static InteractiveConfigCommandContext Context(
        Func<ChatCommandOptions, ResolvedTianShuConfig>? loadResolvedConfig = null,
        Func<bool>? hasRunningConversation = null,
        Func<string?>? getCurrentThreadId = null,
        Action<string?>? setSessionActiveThreadId = null,
        Action<string?>? setCurrentDisplayModel = null,
        Action? markTerminalTurn = null,
        Action<string, bool>? writeLine = null,
        Action<string, bool>? writeControlPlaneLine = null,
        Func<string?>? resolveConfigGuiExecutable = null,
        Func<string, bool>? launchConfigGui = null)
        => new(
            loadResolvedConfig ?? (static _ => new ResolvedTianShuConfig()),
            hasRunningConversation ?? (static () => false),
            getCurrentThreadId ?? (static () => null),
            setSessionActiveThreadId ?? (static _ => { }),
            setCurrentDisplayModel ?? (static _ => { }),
            markTerminalTurn ?? (static () => { }),
            writeLine ?? (static (_, _) => { }),
            writeControlPlaneLine ?? (static (_, _) => { }),
            resolveConfigGuiExecutable ?? (static () => null),
            launchConfigGui ?? (static _ => false));
}
