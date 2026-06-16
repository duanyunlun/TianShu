using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelJsReplManagerTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldPersistTopLevelBindingsAndSupportTopLevelAwait()
    {
        await using var manager = CreateManager();

        var first = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest(
                "const { liveContext, session } = await Promise.resolve({ liveContext: 41, session: 1 }); console.log(liveContext + session);",
                null),
            StubToolInvoker,
            CancellationToken.None);
        var second = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest("console.log(liveContext + session);", null),
            StubToolInvoker,
            CancellationToken.None);

        Assert.True(first.Success, first.Output);
        Assert.True(second.Success, second.Output);
        Assert.Contains("42", first.Output, StringComparison.Ordinal);
        Assert.Contains("42", second.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPreserveInitializedBindingsAcrossFailedCell()
    {
        await using var manager = CreateManager();

        var first = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest("const base = 40; console.log(base);", null),
            StubToolInvoker,
            CancellationToken.None);
        var second = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest("const { session } = await Promise.resolve({ session: 2 }); throw new Error('boom'); const late = 99;", null),
            StubToolInvoker,
            CancellationToken.None);
        var third = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest("console.log(base + session, typeof late);", null),
            StubToolInvoker,
            CancellationToken.None);

        Assert.True(first.Success, first.Output);
        Assert.False(second.Success, second.Output);
        Assert.True(third.Success, third.Output);
        Assert.Contains("40", first.Output, StringComparison.Ordinal);
        Assert.Contains("boom", second.Output, StringComparison.Ordinal);
        Assert.Contains("42 undefined", third.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetAsync_ShouldClearPersistedBindings()
    {
        await using var manager = CreateManager();

        _ = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest("const sticky = await Promise.resolve(41);", null),
            StubToolInvoker,
            CancellationToken.None);
        var beforeReset = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest("console.log(sticky + 1);", null),
            StubToolInvoker,
            CancellationToken.None);

        await manager.ResetAsync(CancellationToken.None);

        var afterReset = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest("console.log(typeof sticky);", null),
            StubToolInvoker,
            CancellationToken.None);

        Assert.True(beforeReset.Success, beforeReset.Output);
        Assert.True(afterReset.Success, afterReset.Output);
        Assert.Contains("42", beforeReset.Output, StringComparison.Ordinal);
        Assert.Contains("undefined", afterReset.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBlockStaticAndSensitiveImports_AndHideProcess()
    {
        await using var manager = CreateManager();

        var staticImport = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest("import value from './foo.js';", null),
            StubToolInvoker,
            CancellationToken.None);
        var blockedImport = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest("await import(\"node:process\");", null),
            StubToolInvoker,
            CancellationToken.None);
        var hiddenProcess = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest("console.log(typeof process);", null),
            StubToolInvoker,
            CancellationToken.None);

        Assert.False(staticImport.Success, staticImport.Output);
        Assert.Contains("Top-level static import \"./foo.js\" is not supported in js_repl", staticImport.Output, StringComparison.Ordinal);
        Assert.False(blockedImport.Success, blockedImport.Output);
        Assert.Contains("Importing module \"node:process\" is not allowed in js_repl", blockedImport.Output, StringComparison.Ordinal);
        Assert.True(hiddenProcess.Success, hiddenProcess.Output);
        Assert.Contains("undefined", hiddenProcess.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldInvokeHostToolsAndEmitImages()
    {
        await using var manager = CreateManager();
        KernelJsReplToolCall? observedToolCall = null;

        var result = await manager.ExecuteAsync(
            new KernelJsReplExecutionRequest(
                "const out = await tianshu.tool(\"view_image\", { path: \"demo.png\" }); console.log(out.type); await tianshu.emitImage(out);",
                null),
            (call, cancellationToken) =>
            {
                observedToolCall = call;
                var response = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = call.RequestId,
                    ["output"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "input_image",
                            ["image_url"] = "data:image/png;base64,QUJDRA==",
                        },
                    },
                    ["success"] = true,
                });
                return Task.FromResult(new KernelJsReplHostToolResponse(true, response, null));
            },
            CancellationToken.None);

        Assert.NotNull(observedToolCall);
        Assert.Equal("view_image", observedToolCall!.ToolName);
        Assert.Equal(JsonValueKind.Object, observedToolCall.Arguments.ValueKind);
        Assert.Equal("demo.png", observedToolCall.Arguments.GetProperty("path").GetString());
        Assert.True(result.Success, result.Output);
        Assert.Contains("function_call_output", result.Output, StringComparison.Ordinal);
        var image = Assert.Single(result.ContentItems);
        Assert.Equal("input_image", image.Type);
        Assert.Equal("data:image/png;base64,QUJDRA==", image.ImageUrl);
    }

    [Fact]
    public void ParseFreeformInput_ShouldSupportPragmaAndRejectWrappedPayloads()
    {
        var parsed = KernelJsReplRuntimeSupport.ParseFreeformInput("// tianshu-js-repl: timeout_ms=15000\nconsole.log('ok');");
        var wrapped = KernelJsReplRuntimeSupport.ParseFreeformInput("{\"code\":\"await doThing()\"}");

        Assert.True(parsed.Success);
        Assert.NotNull(parsed.Request);
        Assert.Equal(15_000, parsed.Request!.TimeoutMs);
        Assert.Equal("console.log('ok');", parsed.Request.Code);
        Assert.False(wrapped.Success);
        Assert.Contains("js_repl is a freeform tool", wrapped.Error, StringComparison.Ordinal);
    }

    private static KernelJsReplManager CreateManager()
    {
        return new KernelJsReplManager(new KernelJsReplOptions(
            NodePath: "node",
            WorkingDirectory: AppContext.BaseDirectory,
            NodeModuleDirectories: Array.Empty<string>()));
    }

    private static Task<KernelJsReplHostToolResponse> StubToolInvoker(KernelJsReplToolCall call, CancellationToken cancellationToken)
    {
        _ = call;
        _ = cancellationToken;
        var response = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["type"] = "function_call_output",
            ["call_id"] = Guid.NewGuid().ToString("N"),
            ["output"] = "stub",
            ["success"] = true,
        });
        return Task.FromResult(new KernelJsReplHostToolResponse(true, response, null));
    }
}
