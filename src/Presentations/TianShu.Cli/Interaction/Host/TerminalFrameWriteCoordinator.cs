namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Serializes human interactive terminal frame writes into one clear/write/restore transaction.
/// 将 human interactive 终端写入串行化为统一的清理、写入、恢复事务。
/// </summary>
internal sealed class TerminalFrameWriteCoordinator(
    IChatOutputTerminalHost terminalHost,
    Func<bool> getAssistantLineOpen)
{
    private readonly IChatOutputTerminalHost terminalHost = terminalHost ?? throw new ArgumentNullException(nameof(terminalHost));
    private readonly Func<bool> getAssistantLineOpen = getAssistantLineOpen ?? throw new ArgumentNullException(nameof(getAssistantLineOpen));

    public void WriteRetainedText(string text, bool isError)
        => WriteWithPromptRestore(() => WriteRetainedTextInCurrentFrame(text, isError));

    public void WritePresentationBlock(ChatPresentationBlock block, bool isError)
        => WriteWithPromptRestore(() => WritePresentationBlockInCurrentFrame(block, isError));

    public void WriteBlankLine()
        => WriteWithPromptRestore(WriteBlankLineInCurrentFrame);

    public void RenderAssistantRetainedTailFrame()
        => terminalHost.RenderAssistantRetainedTailFrameUnsafe();

    public void WriteRetainedTextInCurrentFrame(string text, bool isError)
        => terminalHost.WriteHumanTerminalRetainedText(text, isError);

    public void WritePresentationBlockInCurrentFrame(ChatPresentationBlock block, bool isError)
        => terminalHost.WriteHumanTerminalPresentationBlock(block, isError);

    public static void WriteBlankLineInCurrentFrame()
        => Console.WriteLine();

    public void RenderAssistantRetainedTailFrameInCurrentFrame()
        => terminalHost.RenderAssistantRetainedTailFrameUnsafe();

    public void RefreshFinalDock()
        => terminalHost.RefreshAndRestoreInlineTailPrompt();

    public void ClearCurrentFrameForOutput()
        => terminalHost.PrepareInlineTailPromptWrite(getAssistantLineOpen());

    public IDisposable BeginExclusiveFrameScope()
    {
        ClearCurrentFrameForOutput();
        return new ExclusiveFrameScope(this);
    }

    private void WriteWithPromptRestore(Action write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var restoreInlinePrompt = terminalHost.PrepareInlineTailPromptWrite(getAssistantLineOpen());
        write();
        if (restoreInlinePrompt)
        {
            terminalHost.RestoreInlineTailPrompt();
        }
    }

    private sealed class ExclusiveFrameScope(TerminalFrameWriteCoordinator owner) : IDisposable
    {
        private TerminalFrameWriteCoordinator? owner = owner;

        public void Dispose()
        {
            var currentOwner = Interlocked.Exchange(ref owner, null);
            currentOwner?.RefreshFinalDock();
        }
    }
}
