namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// 工具执行通知 hook，用于把工具生命周期事件投影给宿主输出层。
/// Tool execution notification hook that projects tool lifecycle events to host output.
/// </summary>
internal sealed class NotificationToolExecutionHook : IKernelToolExecutionHook
{
    private readonly Func<string, object, CancellationToken, Task> notifier;

    public string Name => "notification";

    public NotificationToolExecutionHook(Func<string, object, CancellationToken, Task> notifier)
    {
        this.notifier = notifier;
    }

    public Task OnBeforeExecuteAsync(KernelToolExecutionHookContext context, CancellationToken cancellationToken)
    {
        var callId = context.ExternalCallId ?? context.ItemId;
        return notifier(
            "item/tool/hook",
            new
            {
                threadId = context.ThreadId,
                turnId = context.TurnId,
                itemId = context.ItemId,
                callId,
                toolName = context.ToolName,
                phase = "before",
            },
            cancellationToken);
    }

    public async Task<KernelToolExecutionHookAfterDecision> OnAfterExecuteAsync(
        KernelToolExecutionHookContext context,
        KernelToolResult result,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var callId = context.ExternalCallId ?? context.ItemId;
        await notifier(
            "item/tool/hook",
            new
            {
                threadId = context.ThreadId,
                turnId = context.TurnId,
                itemId = context.ItemId,
                callId,
                toolName = context.ToolName,
                phase = "after",
                status = result.Success ? "completed" : "failed",
                durationMs = (long)Math.Max(0, duration.TotalMilliseconds),
            },
            cancellationToken).ConfigureAwait(false);

        return KernelToolExecutionHookAfterDecision.Continue;
    }

    public Task OnExecuteErrorAsync(
        KernelToolExecutionHookContext context,
        string error,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var callId = context.ExternalCallId ?? context.ItemId;
        return notifier(
            "item/tool/hook",
            new
            {
                threadId = context.ThreadId,
                turnId = context.TurnId,
                itemId = context.ItemId,
                callId,
                toolName = context.ToolName,
                phase = "error",
                error,
                durationMs = (long)Math.Max(0, duration.TotalMilliseconds),
            },
            cancellationToken);
    }
}
