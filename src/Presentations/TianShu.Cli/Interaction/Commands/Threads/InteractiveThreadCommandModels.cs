using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Commands.Threads;

/// <summary>
/// Thread command handler 的宿主回调集合。
/// Host callbacks used by the thread command handler.
/// </summary>
internal sealed record InteractiveThreadCommandContext(
    Func<bool> HasRunningConversation,
    Func<string, bool> IsCurrentThread,
    Action ClearCurrentThreadState,
    Action<ThreadId> ActivateThread,
    Func<string, string?, string, CancellationToken, Task<bool>> ConfirmDestructiveOperationAsync,
    Func<string, CancellationToken, Task<ControlPlaneThreadSnapshot?>> ResumeThreadByIdAsync,
    Action<string> ClearInputHistoryForThread,
    Action ClearAllInputHistory,
    Action<string, bool> WriteLine);
