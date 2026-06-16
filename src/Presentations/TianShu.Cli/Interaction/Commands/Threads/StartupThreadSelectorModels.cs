using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction.Commands.Threads;

/// <summary>
/// Startup thread selection 的最终执行结果。
/// Outcome returned after resolving and applying startup thread selection.
/// </summary>
internal enum StartupThreadActionOutcome
{
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>
/// Startup thread selector 需要宿主提供的交互与状态回调。
/// Host callbacks required by the startup thread selector.
/// </summary>
internal sealed record StartupThreadSelectorContext(
    Func<string> GetCurrentDirectory,
    Func<bool> ShouldUseTerminalThreadPicker,
    Func<IReadOnlyList<ControlPlaneThreadSummary>, bool, string, CancellationToken, Task<ControlPlaneThreadSummary?>> SelectThreadWithTerminalAsync,
    Func<CancellationToken, Task<string?>> ReadLineAsync,
    Action<ControlPlaneThreadSnapshot, CancellationToken> ConsumeResumedThreadState,
    Func<(int Approvals, int Permissions, int UserInputs)> GetPendingInteractiveCounts,
    Action<string, bool> WriteLine);
