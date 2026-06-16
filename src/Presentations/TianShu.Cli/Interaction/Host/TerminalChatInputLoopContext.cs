using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Provides the typed dependencies required by the terminal chat input loop.
/// 提供终端 chat 输入循环所需的类型化依赖。
/// </summary>
internal sealed record TerminalChatInputLoopContext
{
    public required ChatCommandOptions Options { get; init; }

    public IReadOnlyList<string> InitialInputHistory { get; init; } = [];

    public Func<string?> GetInputHistoryScopeKey { get; init; } = static () => null;

    public Func<string?, IReadOnlyList<string>> LoadInputHistory { get; init; } = static _ => [];

    public required Func<string> BuildPrompt { get; init; }

    public required Action<TerminalChatComposer, TerminalPromptRenderer, string, IReadOnlyList<string>?, string?> RenderPrompt { get; init; }

    public required Action<TerminalPromptRenderer?, bool, string?> CompleteInputLine { get; init; }

    public required Func<int, bool> MoveQueuedFollowUpSelection { get; init; }

    public required Func<CancellationToken, Task<bool>> PromoteSelectedQueuedFollowUpAsync { get; init; }

    public Action<string, TerminalSubmitIntent> RecordSubmittedInput { get; init; } = static (_, _) => { };

    public required Func<string, TerminalSubmitIntent, CancellationToken, Task<bool>> SubmitLineAsync { get; init; }

    public required Action ResetTerminal { get; init; }
}
