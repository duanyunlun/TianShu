using TianShu.Cli.Interaction.Commands.Model;
using TianShu.Cli.Interaction.Commands.ModelStatus;
using TianShu.Configuration;

namespace TianShu.Cli.Tests;

public sealed class InteractiveModelCommandHandlerTests
{
    [Fact]
    public async Task HandleModelCommandAsync_WhenStatusCommand_DelegatesToStatusHandler()
    {
        var handler = new InteractiveModelCommandHandler();
        var calls = new List<ModelStatusMode>();

        await handler.HandleModelCommandAsync(
            new CliConsumerFakeRuntime(),
            new ChatCommandOptions(),
            "status --matrix",
            Context(handleModelStatus: (mode, _) =>
            {
                calls.Add(mode);
                return Task.CompletedTask;
            }),
            CancellationToken.None);

        Assert.Equal([ModelStatusMode.Matrix], calls);
    }

    [Fact]
    public async Task HandleModelCommandAsync_WhenNoCurrentThread_SetsDefaultRouteSet()
    {
        var handler = new InteractiveModelCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        var runtime = new CliConsumerFakeRuntime();
        var options = new ChatCommandOptions { RuntimeModelProvider = "old_provider" };
        string? displayModel = null;

        await handler.HandleModelCommandAsync(
            runtime,
            options,
            "fast",
            Context(
                setCurrentDisplayModel: model => displayModel = model,
                writeControlPlaneLine: (text, isError) => output.Add((text, isError))),
            CancellationToken.None);

        Assert.Null(options.RuntimeModel);
        Assert.Equal("old_provider", options.RuntimeModelProvider);
        Assert.Null(displayModel);
        Assert.Empty(runtime.StartThreadRequestCalls);
        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("已选择模型路由方案：fast", StringComparison.Ordinal));
        var configWrite = Assert.Single(runtime.ConfigBatchWriteCalls);
        var item = Assert.Single(configWrite.Items);
        Assert.Equal("profiles.default.model_route_set", item.KeyPath);
        Assert.Equal("fast", item.Value?.StringValue);
        Assert.True(configWrite.ReloadUserConfig);
    }

    [Fact]
    public async Task HandleModelCommandAsync_WhenCurrentThreadRunning_RejectsRouteSetSwitch()
    {
        var handler = new InteractiveModelCommandHandler();
        var runtime = new CliConsumerFakeRuntime();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleModelCommandAsync(
            runtime,
            new ChatCommandOptions(),
            "fast",
            Context(
                getCurrentThreadId: static () => "thread_1",
                hasRunningConversation: static () => true,
                writeControlPlaneLine: (text, isError) => output.Add((text, isError))),
            CancellationToken.None);

        Assert.Empty(runtime.ResumeThreadRequestCalls);
        Assert.Contains(output, static line => line.IsError && line.Text.Contains("当前回合仍在运行", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleModelCommandAsync_WhenCurrentThreadResumeSucceeds_UpdatesRouteSet()
    {
        var runtime = new CliConsumerFakeRuntime
        {
            ResumeThreadRequestAsyncHandler = static (request, _) => Task.FromResult<AgentThreadResumeResult?>(new AgentThreadResumeResult
            {
                ThreadId = request.ThreadId.Value,
                SessionConfiguration = new AgentThreadSessionConfiguration(),
            }),
        };
        var handler = new InteractiveModelCommandHandler();
        var options = new ChatCommandOptions();
        string? activeThreadId = null;
        string? displayModel = null;
        var markedTerminalTurn = false;

        await handler.HandleModelCommandAsync(
            runtime,
            options,
            "fast",
            Context(
                getCurrentThreadId: static () => "thread_1",
                setSessionActiveThreadId: value => activeThreadId = value,
                setCurrentDisplayModel: model => displayModel = model,
                markTerminalTurn: () => markedTerminalTurn = true),
            CancellationToken.None);

        var request = Assert.Single(runtime.ResumeThreadRequestCalls);
        Assert.Equal("thread_1", request.ThreadId.Value);
        Assert.Null(request.Model);
        Assert.Null(request.ModelProvider);
        Assert.Equal("fast", request.Configuration?["model_route_set"].StringValue);
        Assert.Null(options.RuntimeModel);
        Assert.Equal("thread_1", activeThreadId);
        Assert.Null(displayModel);
        Assert.True(markedTerminalTurn);
        var configWrite = Assert.Single(runtime.ConfigBatchWriteCalls);
        var item = Assert.Single(configWrite.Items);
        Assert.Equal("profiles.default.model_route_set", item.KeyPath);
        Assert.Equal("fast", item.Value?.StringValue);
        Assert.True(configWrite.ReloadUserConfig);
    }

    [Fact]
    public async Task HandleModelCommandAsync_WhenNoRouteSetArgument_ListsRouteSets()
    {
        var runtime = new CliConsumerFakeRuntime();
        var handler = new InteractiveModelCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleModelCommandAsync(
            runtime,
            new ChatCommandOptions { RuntimeModel = "current_model" },
            string.Empty,
            Context(writeControlPlaneLine: (text, isError) => output.Add((text, isError))),
            CancellationToken.None);

        Assert.Empty(runtime.ModelListCalls);
        Assert.Contains(output, static line => line.Text == "当前模型路由方案：default");
        Assert.Contains(output, static line => line.Text.Contains("* default", StringComparison.Ordinal));
        Assert.Contains(output, static line => line.Text.Contains("- fast", StringComparison.Ordinal));
    }

    private static InteractiveModelCommandContext Context(
        Func<ModelStatusMode, CancellationToken, Task>? handleModelStatus = null,
        Func<ChatCommandOptions, ResolvedTianShuConfig>? loadResolvedConfig = null,
        Func<bool>? shouldUseInteractivePicker = null,
        Func<bool>? hasRunningConversation = null,
        Func<string?>? getCurrentThreadId = null,
        Action<string?>? setSessionActiveThreadId = null,
        Action<string?>? setCurrentDisplayModel = null,
        Action? markTerminalTurn = null,
        Action<string, bool>? writeControlPlaneLine = null)
        => new(
            handleModelStatus ?? (static (_, _) => Task.CompletedTask),
            loadResolvedConfig ?? (static _ => BuildRouteSetConfig()),
            shouldUseInteractivePicker ?? (static () => false),
            hasRunningConversation ?? (static () => false),
            getCurrentThreadId ?? (static () => null),
            setSessionActiveThreadId ?? (static _ => { }),
            setCurrentDisplayModel ?? (static _ => { }),
            markTerminalTurn ?? (static () => { }),
            writeControlPlaneLine ?? (static (_, _) => { }),
            null);

    private static ResolvedTianShuConfig BuildRouteSetConfig()
        => new()
        {
            RawConfig = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["model_route_set"] = "default",
                ["model_route_sets"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["default"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["display_name"] = "Default Route Set",
                        ["routes"] = new object?[]
                        {
                            new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["kind"] = "default",
                                ["candidates"] = new object?[]
                                {
                                    new Dictionary<string, object?>(StringComparer.Ordinal)
                                    {
                                        ["provider"] = "openai-compatible",
                                        ["model"] = "model_a",
                                        ["protocol"] = "auto",
                                    },
                                },
                            },
                        },
                    },
                    ["fast"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["display_name"] = "Fast Route Set",
                        ["routes"] = new object?[]
                        {
                            new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["kind"] = "default",
                                ["candidates"] = new object?[]
                                {
                                    new Dictionary<string, object?>(StringComparer.Ordinal)
                                    {
                                        ["provider"] = "openai-compatible",
                                        ["model"] = "model_b",
                                        ["protocol"] = "auto",
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
}
