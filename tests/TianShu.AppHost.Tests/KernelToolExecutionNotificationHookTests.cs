using System.Text.Json;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolExecutionNotificationHookTests
{
    [Fact]
    public async Task OnBeforeExecuteAsync_WhenExternalCallIdExists_ShouldProjectProviderCallId()
    {
        var notifications = new List<(string Method, JsonElement Payload)>();
        var hook = new NotificationToolExecutionHook((method, payload, _) =>
        {
            notifications.Add((method, JsonSerializer.SerializeToElement(payload)));
            return Task.CompletedTask;
        });

        await hook.OnBeforeExecuteAsync(
            new KernelToolExecutionHookContext(
                ThreadId: "thread-1",
                TurnId: "turn-1",
                ItemId: "tool_shell_call_MDOODZHNl08XLvhyxJoZkFBQ",
                ExternalCallId: "call_MDOODZHNl08XLvhyxJoZkFBQ",
                ToolName: "shell",
                Arguments: JsonSerializer.SerializeToElement(new { command = "Write-Output OK" })),
            CancellationToken.None);

        var notification = Assert.Single(notifications);
        Assert.Equal("item/tool/hook", notification.Method);
        Assert.Equal("tool_shell_call_MDOODZHNl08XLvhyxJoZkFBQ", notification.Payload.GetProperty("itemId").GetString());
        Assert.Equal("call_MDOODZHNl08XLvhyxJoZkFBQ", notification.Payload.GetProperty("callId").GetString());
    }

    [Fact]
    public async Task OnBeforeExecuteAsync_WhenExternalCallIdMissing_ShouldFallbackToItemId()
    {
        var notifications = new List<JsonElement>();
        var hook = new NotificationToolExecutionHook((_, payload, _) =>
        {
            notifications.Add(JsonSerializer.SerializeToElement(payload));
            return Task.CompletedTask;
        });

        await hook.OnBeforeExecuteAsync(
            new KernelToolExecutionHookContext(
                ThreadId: "thread-1",
                TurnId: "turn-1",
                ItemId: "tool_inline_turn-1",
                ExternalCallId: null,
                ToolName: "read_file",
                Arguments: JsonSerializer.SerializeToElement(new { path = "README.md" })),
            CancellationToken.None);

        var payload = Assert.Single(notifications);
        Assert.Equal("tool_inline_turn-1", payload.GetProperty("itemId").GetString());
        Assert.Equal("tool_inline_turn-1", payload.GetProperty("callId").GetString());
    }
}
