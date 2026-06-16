using TianShu.Cli.Interaction.Presenters;
using TianShu.Cli.Interaction.Recording;
using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Carries dependencies and state callbacks for <see cref="ChatOutputWriter"/>.
/// 承载 <see cref="ChatOutputWriter"/> 的依赖与状态回调，避免 writer 直接持有 runner 细节。
/// </summary>
internal sealed class ChatOutputWriterContext
{
    public required object ConsoleGate { get; init; }

    public required IChatOutputTerminalHost TerminalHost { get; init; }

    public required Func<InteractionPipeline> GetPresentationPipeline { get; init; }

    public required Func<ChatOutputProtocol> GetOutputProtocol { get; init; }

    public required Func<bool> IsScriptMode { get; init; }

    public required Func<PlanDockSummary?> GetCurrentPlanDockSummary { get; init; }

    public required Action<string, bool, CliTranscriptRecordKind, bool> AppendTranscript { get; init; }

    public required Action MarkFailure { get; init; }

    public required Action<string?> SetLastFailureMessage { get; init; }

    public required Func<bool> GetAssistantLineOpen { get; init; }

    public required Action<bool> SetAssistantLineOpen { get; init; }

    public required Action<string?> SetLastCompletedAssistantText { get; init; }

    public required Action<bool> SetAssistantLeadingSpacerPending { get; init; }
}
