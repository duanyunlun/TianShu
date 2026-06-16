using System.Text.Json;
using TianShu.Cli.Interaction.Commands.Rpc;

namespace TianShu.Cli.Tests;

public sealed class InteractiveRpcCommandHandlerTests
{
    [Fact]
    public async Task HandleRpcAsync_WhenMethodMissing_WritesUsage()
    {
        var handler = new InteractiveRpcCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleRpcAsync(
            new CliConsumerFakeRuntime(),
            string.Empty,
            JsonOptions(),
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Contains(output, static line => line.IsError && line.Text == "用法：/rpc <method> [params-json]");
    }

    [Fact]
    public async Task HandleRpcAsync_WhenParamsJsonInvalid_WritesParseError()
    {
        var handler = new InteractiveRpcCommandHandler();
        var runtime = new CliConsumerFakeRuntime();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleRpcAsync(
            runtime,
            "tool/ping {",
            JsonOptions(),
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Empty(runtime.RpcCalls);
        Assert.Contains(output, static line => line.IsError && line.Text.StartsWith("解析 RPC 参数 JSON 失败：", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleRpcAsync_WhenFormalMethodRequested_UsesTypedControlPlaneRequest()
    {
        var handler = new InteractiveRpcCommandHandler();
        var runtime = new CliConsumerFakeRuntime();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleRpcAsync(
            runtime,
            "diagnostics/trace/read {\"traceId\":\"trace-chat-formal-1\"}",
            JsonOptions(),
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Contains(output, static line => !line.IsError && line.Text.Contains("trace-chat-formal-1", StringComparison.Ordinal));
        Assert.Collection(
            runtime.ExecutionTraceCalls,
            call => Assert.Equal("trace-chat-formal-1", call.TraceId.Value));
        Assert.Empty(runtime.RpcCalls);
    }

    [Fact]
    public async Task HandleRpcAsync_WhenMethodIsNotFormalized_WritesUnavailableMessage()
    {
        var handler = new InteractiveRpcCommandHandler();
        var runtime = new CliConsumerFakeRuntime();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleRpcAsync(
            runtime,
            "diagnostics/ping {\"value\":42}",
            JsonOptions(),
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Empty(runtime.RpcCalls);
        Assert.Contains(output, static line => line.IsError && line.Text.Contains("正式 runtime surface", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleRpcAsync_WhenKernelOnlyToolMethodRequested_DoesNotUseDiagnosticsFallback()
    {
        var handler = new InteractiveRpcCommandHandler();
        var runtime = new CliConsumerFakeRuntime
        {
            InvokeDiagnosticRpcAsyncHandler = static (_, _, _) =>
                throw new Xunit.Sdk.XunitException("未 formalized 的 tool method 不应落到 diagnostics fallback。"),
        };
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleRpcAsync(
            runtime,
            "tool/ping",
            JsonOptions(),
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Empty(runtime.RpcCalls);
        Assert.Contains(output, static line => line.IsError && line.Text.Contains("正式 runtime surface", StringComparison.Ordinal));
    }

    private static JsonSerializerOptions JsonOptions()
        => new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

}
