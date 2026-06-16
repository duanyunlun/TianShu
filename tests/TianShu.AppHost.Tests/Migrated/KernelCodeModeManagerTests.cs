using System.Reflection;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelCodeModeManagerTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldStoreAndLoadValuesAcrossExecCalls()
    {
        await using var manager = CreateManager();
        manager.ActivateTurn("turn_store_1", StubToolInvoker, CancellationToken.None);

        var stored = await manager.ExecuteAsync(
            new KernelCodeModeExecutionRequest(
                """
                store("nb", { title: "Notebook", items: [1, true, null] });
                text("stored");
                """,
                null,
                null),
            [],
            CancellationToken.None);

        manager.DeactivateTurn("turn_store_1");
        manager.ActivateTurn("turn_store_2", StubToolInvoker, CancellationToken.None);

        var loaded = await manager.ExecuteAsync(
            new KernelCodeModeExecutionRequest(
                """
                text(JSON.stringify(load("nb")));
                """,
                null,
                null),
            [],
            CancellationToken.None);

        Assert.True(stored.Success, stored.Output);
        Assert.True(loaded.Success, loaded.Output);

        var storedTexts = GetTextItems(stored);
        var loadedTexts = GetTextItems(loaded);
        Assert.Equal("stored", Assert.Single(storedTexts.Skip(1)));

        var parsed = JsonDocument.Parse(Assert.Single(loadedTexts.Skip(1))).RootElement;
        Assert.Equal("Notebook", parsed.GetProperty("title").GetString());
        Assert.Equal(3, parsed.GetProperty("items").GetArrayLength());
        Assert.Equal(1, parsed.GetProperty("items")[0].GetInt32());
        Assert.True(parsed.GetProperty("items")[1].GetBoolean());
        Assert.Equal(JsonValueKind.Null, parsed.GetProperty("items")[2].ValueKind);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldYieldAndResumeWithWaitAsync()
    {
        await using var manager = CreateManager();
        manager.ActivateTurn("turn_wait_1", StubToolInvoker, CancellationToken.None);

        var first = await manager.ExecuteAsync(
            new KernelCodeModeExecutionRequest(
                """
                text("phase 1");
                yield_control();
                text("phase 2");
                """,
                null,
                null),
            [],
            CancellationToken.None);

        var firstTexts = GetTextItems(first);
        Assert.True(first.Success, first.Output);
        Assert.StartsWith("Script running with cell ID ", firstTexts[0], StringComparison.Ordinal);
        Assert.Equal("phase 1", firstTexts[1]);

        var cellId = ExtractCellId(firstTexts[0]);

        manager.DeactivateTurn("turn_wait_1");
        manager.ActivateTurn("turn_wait_2", StubToolInvoker, CancellationToken.None);

        var second = await manager.WaitAsync(
            new KernelCodeModeWaitRequest(
                cellId,
                KernelCodeModeManager.DefaultWaitYieldTimeMs,
                null,
                false),
            CancellationToken.None);

        var secondTexts = GetTextItems(second);
        Assert.True(second.Success, second.Output);
        Assert.StartsWith("Script completed", secondTexts[0], StringComparison.Ordinal);
        Assert.Equal("phase 2", Assert.Single(secondTexts.Skip(1)));
    }

    [Fact]
    public async Task WaitAsync_ShouldTerminateYieldedBusyLoop()
    {
        await using var manager = CreateManager();
        manager.ActivateTurn("turn_terminate_1", StubToolInvoker, CancellationToken.None);

        var first = await manager.ExecuteAsync(
            new KernelCodeModeExecutionRequest(
                """
                text("phase 1");
                yield_control();
                while (true) {}
                """,
                null,
                null),
            [],
            CancellationToken.None);

        var firstTexts = GetTextItems(first);
        Assert.True(first.Success, first.Output);
        Assert.StartsWith("Script running with cell ID ", firstTexts[0], StringComparison.Ordinal);
        Assert.Equal("phase 1", firstTexts[1]);

        var terminated = await manager.WaitAsync(
            new KernelCodeModeWaitRequest(
                ExtractCellId(firstTexts[0]),
                KernelCodeModeManager.DefaultWaitYieldTimeMs,
                null,
                true),
            CancellationToken.None);

        var terminatedTexts = GetTextItems(terminated);
        Assert.True(terminated.Success, terminated.Output);
        Assert.StartsWith("Script terminated", terminatedTexts[0], StringComparison.Ordinal);
        Assert.Single(terminatedTexts);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSupportOutputTextAndOutputImageHelpers()
    {
        await using var manager = CreateManager();
        manager.ActivateTurn("turn_helpers_1", StubToolInvoker, CancellationToken.None);

        var result = await manager.ExecuteAsync(
            new KernelCodeModeExecutionRequest(
                """
                const { output_text, output_image } = await import('@openai/code_mode');
                output_text({ json: true });
                output_image("https://example.com/image.jpg");
                """,
                null,
                null),
            [],
            CancellationToken.None);

        Assert.True(result.Success, result.Output);
        Assert.Equal(3, result.ContentItems.Count);
        Assert.Equal("input_text", result.ContentItems[0].Type);
        Assert.Equal("input_text", result.ContentItems[1].Type);
        Assert.Equal("""{"json":true}""", result.ContentItems[1].Text);
        Assert.Equal("input_image", result.ContentItems[2].Type);
        Assert.Equal("https://example.com/image.jpg", result.ContentItems[2].ImageUrl);
    }

    [Fact]
    public async Task WaitAsync_ShouldUseTokenBudgetForFinalOutput()
    {
        await using var manager = CreateManager();
        manager.ActivateTurn("turn_budget_1", StubToolInvoker, CancellationToken.None);

        var first = await manager.ExecuteAsync(
            new KernelCodeModeExecutionRequest(
                """
                text("phase 1");
                yield_control();
                text("token one token two token three token four token five token six token seven");
                """,
                null,
                100),
            [],
            CancellationToken.None);

        var firstTexts = GetTextItems(first);
        var cellId = ExtractCellId(firstTexts[0]);

        manager.DeactivateTurn("turn_budget_1");
        manager.ActivateTurn("turn_budget_2", StubToolInvoker, CancellationToken.None);

        var second = await manager.WaitAsync(
            new KernelCodeModeWaitRequest(
                cellId,
                KernelCodeModeManager.DefaultWaitYieldTimeMs,
                6,
                false),
            CancellationToken.None);

        var secondTexts = GetTextItems(second);
        Assert.True(second.Success, second.Output);
        Assert.StartsWith("Script completed", secondTexts[0], StringComparison.Ordinal);
        Assert.Contains("tokens truncated", Assert.Single(secondTexts.Skip(1)), StringComparison.Ordinal);
    }

    [Fact]
    public void DeactivateTurn_ShouldKeepToolInvokerForBackgroundWork()
    {
        var manager = CreateManager();
        manager.ActivateTurn("turn_reflection_1", StubToolInvoker, CancellationToken.None);
        manager.DeactivateTurn("turn_reflection_1");

        var managerType = typeof(KernelCodeModeManager);
        var activeTurnField = managerType.GetField("activeTurnId", BindingFlags.Instance | BindingFlags.NonPublic);
        var invokerField = managerType.GetField("activeToolInvoker", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(activeTurnField);
        Assert.NotNull(invokerField);
        Assert.Null(activeTurnField!.GetValue(manager));
        Assert.NotNull(invokerField!.GetValue(manager));
    }

    private static KernelCodeModeManager CreateManager()
    {
        return new KernelCodeModeManager(new KernelCodeModeOptions(
            NodePath: "node",
            WorkingDirectory: AppContext.BaseDirectory));
    }

    private static Task<JsonElement> StubToolInvoker(KernelCodeModeToolCall call, CancellationToken cancellationToken)
    {
        _ = call;
        _ = cancellationToken;
        return Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true }));
    }

    private static List<string> GetTextItems(KernelCodeModeOperationResult result)
    {
        return result.ContentItems
            .Where(static item => string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.Text ?? string.Empty)
            .ToList();
    }

    private static string ExtractCellId(string headerText)
    {
        const string prefix = "Script running with cell ID ";
        Assert.StartsWith(prefix, headerText, StringComparison.Ordinal);
        var remainder = headerText[prefix.Length..];
        var lineBreakIndex = remainder.IndexOf('\n');
        var cellId = lineBreakIndex >= 0 ? remainder[..lineBreakIndex] : remainder;
        return cellId.Trim();
    }
}
