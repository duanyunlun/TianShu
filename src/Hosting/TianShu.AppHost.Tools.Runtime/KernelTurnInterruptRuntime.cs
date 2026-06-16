using System.Text.Json;
using TianShu.AppHost.State;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn interrupt 运行时，负责中断控制入口的状态登记与后台取消。
/// Runtime that owns turn interrupt control state registration and background cancellation.
/// </summary>
internal sealed class KernelTurnInterruptRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelTurnBackgroundSchedulerRuntime backgroundTurnSchedulerRuntime;
    private readonly Func<JsonElement, int, string, object?, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<string?, string?> normalize;
    private readonly Action<string, string> registerPendingTurnInterrupt;
    private readonly Action<string, string, JsonElement> registerPendingTurnInterruptResponse;
    private readonly Action<string, string> clearPendingTurnInterrupt;
    private readonly Action<string?, string?> clearPendingTurnInterruptResponses;

    public KernelTurnInterruptRuntime(
        KernelThreadStore threadStore,
        KernelTurnBackgroundSchedulerRuntime backgroundTurnSchedulerRuntime,
        Func<JsonElement, int, string, object?, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<string?, string?> normalize,
        Action<string, string> registerPendingTurnInterrupt,
        Action<string, string, JsonElement> registerPendingTurnInterruptResponse,
        Action<string, string> clearPendingTurnInterrupt,
        Action<string?, string?> clearPendingTurnInterruptResponses)
    {
        this.threadStore = threadStore ?? throw new ArgumentNullException(nameof(threadStore));
        this.backgroundTurnSchedulerRuntime = backgroundTurnSchedulerRuntime ?? throw new ArgumentNullException(nameof(backgroundTurnSchedulerRuntime));
        this.writeErrorAsync = writeErrorAsync ?? throw new ArgumentNullException(nameof(writeErrorAsync));
        this.writeResultAsync = writeResultAsync ?? throw new ArgumentNullException(nameof(writeResultAsync));
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
        this.registerPendingTurnInterrupt = registerPendingTurnInterrupt ?? throw new ArgumentNullException(nameof(registerPendingTurnInterrupt));
        this.registerPendingTurnInterruptResponse = registerPendingTurnInterruptResponse ?? throw new ArgumentNullException(nameof(registerPendingTurnInterruptResponse));
        this.clearPendingTurnInterrupt = clearPendingTurnInterrupt ?? throw new ArgumentNullException(nameof(clearPendingTurnInterrupt));
        this.clearPendingTurnInterruptResponses = clearPendingTurnInterruptResponses ?? throw new ArgumentNullException(nameof(clearPendingTurnInterruptResponses));
    }

    public async Task HandleTurnInterruptAsync(
        JsonElement id,
        string threadId,
        string turnId,
        CancellationToken cancellationToken)
    {
        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (thread is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", null, cancellationToken).ConfigureAwait(false);
            return;
        }

        var normalizedThreadId = normalize(threadId)!;
        var normalizedTurnId = normalize(turnId)!;
        registerPendingTurnInterrupt(normalizedThreadId, normalizedTurnId);

        if (backgroundTurnSchedulerRuntime.TryCancel(normalizedTurnId))
        {
            registerPendingTurnInterruptResponse(normalizedThreadId, normalizedTurnId, id);
            return;
        }

        clearPendingTurnInterrupt(normalizedThreadId, normalizedTurnId);
        clearPendingTurnInterruptResponses(normalizedThreadId, normalizedTurnId);
        await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
    }
}
