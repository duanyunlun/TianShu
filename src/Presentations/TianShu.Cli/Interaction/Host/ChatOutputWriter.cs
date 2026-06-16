using TianShu.Cli;
using TianShu.Cli.Interaction.Presenters;
using TianShu.Cli.Interaction.Recording;
using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Writes chat presentation output for human, script, and JSONL protocols.
/// 负责将 chat 展示块写入 human、script 与 JSONL 输出协议，避免 runner 直接分叉输出细节。
/// </summary>
internal sealed class ChatOutputWriter(ChatOutputWriterContext context)
{
    private static readonly AsyncLocal<ControlOutputState?> CurrentControlOutputScope = new();
    private readonly ChatOutputWriterContext context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly TerminalFrameWriteCoordinator terminalFrameWriter = new(
        context.TerminalHost,
        context.GetAssistantLineOpen);
    private ControlOutputState? activeBufferedControlOutputScope;
    private string? lastDisplayedErrorMessage;

    public void Reset()
    {
        lastDisplayedErrorMessage = null;
        CurrentControlOutputScope.Value = null;
        activeBufferedControlOutputScope = null;
    }

    public IDisposable BeginExclusiveTerminalFrameScope()
    {
        if (!IsHumanInteractive())
        {
            return NoopDisposable.Instance;
        }

        lock (context.ConsoleGate)
        {
            terminalFrameWriter.ClearCurrentFrameForOutput();
        }

        return new ExclusiveTerminalFrameScope(context.ConsoleGate, terminalFrameWriter);
    }

    public IDisposable BeginControlOutputScope(bool buffered = false, bool queueExternalOutput = true)
    {
        var state = new ControlOutputState(CurrentControlOutputScope.Value, buffered && IsHumanInteractive(), queueExternalOutput);
        CurrentControlOutputScope.Value = state;
        if (state.Buffered && state.QueueExternalOutput)
        {
            lock (context.ConsoleGate)
            {
                activeBufferedControlOutputScope ??= state;
            }
        }

        return new ControlOutputScope(this, state);
    }

    public string RenderPresentationBlockText(ChatPresentationBlock block, bool styled)
        => string.Join(
            Environment.NewLine,
            context.GetPresentationPipeline()
                .BuildPresentationBlockFrame(
                    block,
                    context.GetCurrentPlanDockSummary(),
                    context.TerminalHost.BuildTerminalFrameContextFromDock(
                        context.TerminalHost.BuildCurrentComposerDockState(),
                        includePopupLines: false,
                        styled))
                .TranscriptLines);

    public void Write(string text)
    {
        context.AppendTranscript(text, false, CliTranscriptRecordKind.AssistantText, false);
        lock (context.ConsoleGate)
        {
            if (context.GetOutputProtocol() == ChatOutputProtocol.Jsonl)
            {
                ChatJsonlOutputWriter.WriteStdout(text, partial: true);
                return;
            }

            if (IsHumanInteractive())
            {
                if (TryQueueHumanOutputBehindControlBarrier(
                        QueuedHumanOutput.AssistantRetainedFrame(),
                        includeCurrentScope: true))
                {
                    return;
                }

                terminalFrameWriter.RenderAssistantRetainedTailFrame();
                return;
            }

            Console.Write(text);
        }
    }

    public void WriteDisplayLine(string plainText, string displayText)
    {
        context.AppendTranscript(
            plainText,
            true,
            CliTranscriptRecordKind.ActionableStatus,
            false);
        lock (context.ConsoleGate)
        {
            if (context.GetOutputProtocol() == ChatOutputProtocol.Jsonl)
            {
                ChatJsonlOutputWriter.WriteStdout(plainText, partial: false);
                return;
            }

            if (IsHumanInteractive())
            {
                if (TryQueueHumanOutputBehindControlBarrier(QueuedHumanOutput.RetainedText(displayText, isError: false)))
                {
                    return;
                }

                terminalFrameWriter.WriteRetainedText(displayText, isError: false);
            }
            else
            {
                Console.WriteLine(displayText);
            }
        }
    }

    public void WriteDisplayBlock(
        ChatPresentationBlock block,
        bool isError = false,
        bool? countAsFailure = null,
        CliTranscriptRecordKind? transcriptKind = null)
    {
        var plainText = RenderPresentationBlockText(block, styled: false);
        var displayText = RenderPresentationBlockText(block, styled: true);
        if (countAsFailure ?? isError)
        {
            context.MarkFailure();
            if (!string.IsNullOrWhiteSpace(plainText))
            {
                context.SetLastFailureMessage(plainText);
            }
        }

        context.AppendTranscript(
            plainText,
            true,
            transcriptKind ?? (isError ? CliTranscriptRecordKind.Error : CliTranscriptRecordKind.ActionableStatus),
            isError);
        lock (context.ConsoleGate)
        {
            if (context.GetOutputProtocol() == ChatOutputProtocol.Jsonl)
            {
                ChatJsonlOutputWriter.Write(isError ? "stderr" : "stdout", plainText, partial: false);
                return;
            }

            if (IsHumanInteractive())
            {
                if (IsControlOutputScopeActive() && ShouldRenderDisplayBlockAsControlOutput(block, isError))
                {
                    WriteHumanControlOutput(plainText, isError);
                }
                else if (TryQueueHumanOutputBehindControlBarrier(
                             QueuedHumanOutput.PresentationBlock(block, isError),
                             includeCurrentScope: true))
                {
                    return;
                }
                else
                {
                    terminalFrameWriter.WritePresentationBlock(block, isError);
                }
            }
            else if (isError)
            {
                Console.Error.WriteLine(displayText);
            }
            else
            {
                Console.WriteLine(displayText);
            }
        }
    }

    public void WriteProjectionCommittedBlocks(
        IReadOnlyList<ChatPresentationBlock> blocks,
        bool countErrorsAsFailure = true)
    {
        if (blocks.Count == 0)
        {
            return;
        }

        foreach (var block in blocks)
        {
            switch (block)
            {
                case AssistantMessageBlock assistant:
                    WriteCommittedAssistantBlock(assistant);
                    break;
                case SystemNoticeBlock notice:
                    WriteErrorLineOnce(notice.Text, countErrorsAsFailure);
                    break;
                case ToolInvocationBlock:
                    WriteDisplayBlock(block);
                    if (IsHumanInteractive())
                    {
                        context.SetAssistantLeadingSpacerPending(true);
                    }

                    break;
                default:
                    WriteDisplayBlock(block);
                    break;
            }
        }
    }

    public void WriteCommittedAssistantBlock(AssistantMessageBlock block)
    {
        if (string.IsNullOrWhiteSpace(block.Text))
        {
            context.SetAssistantLineOpen(false);
            return;
        }

        context.SetLastCompletedAssistantText(block.Text);
        var queuedForBarrier = false;
        lock (context.ConsoleGate)
        {
            if (IsHumanInteractive())
            {
                if (TryQueueHumanOutputBehindControlBarrier(
                        QueuedHumanOutput.PresentationBlock(block, isError: false, refreshAfter: true),
                        includeCurrentScope: true))
                {
                    queuedForBarrier = true;
                }
                else
                {
                    terminalFrameWriter.WritePresentationBlock(block, isError: false);
                }
            }
            else if (context.GetAssistantLineOpen())
            {
                Console.WriteLine();
            }
        }

        context.SetAssistantLineOpen(false);
        if (!queuedForBarrier)
        {
            terminalFrameWriter.RefreshFinalDock();
        }
    }

    public void WriteTerminalVisualSpacerLine()
    {
        lock (context.ConsoleGate)
        {
            if (IsHumanInteractive())
            {
                if (TryQueueHumanOutputBehindControlBarrier(QueuedHumanOutput.BlankLine(), includeCurrentScope: true))
                {
                    return;
                }

                terminalFrameWriter.WriteBlankLine();
            }
        }
    }

    public void WriteControlPlaneLine(
        string text,
        bool isError = false,
        bool? countAsFailure = null)
        => WriteLine(
            text,
            isError,
            countAsFailure,
            CliTranscriptRecordKind.ActionableStatus,
            includeInTranscript: false);

    public void WriteLine(
        string text,
        bool isError = false,
        bool? countAsFailure = null,
        CliTranscriptRecordKind? transcriptKind = null,
        bool includeInTranscript = true)
    {
        if (countAsFailure ?? isError)
        {
            context.MarkFailure();
            if (!string.IsNullOrWhiteSpace(text))
            {
                context.SetLastFailureMessage(text);
            }
        }

        var kind = transcriptKind ?? (isError ? CliTranscriptRecordKind.Error : CliTranscriptRecordKind.ActionableStatus);
        if (includeInTranscript)
        {
            context.AppendTranscript(text, true, kind, isError);
        }

        lock (context.ConsoleGate)
        {
            if (context.GetOutputProtocol() == ChatOutputProtocol.Jsonl)
            {
                ChatJsonlOutputWriter.Write(isError ? "stderr" : "stdout", text, partial: false);
                return;
            }

            if (context.GetOutputProtocol() == ChatOutputProtocol.Human)
            {
                if (IsHumanInteractive()
                    && !IsControlOutputScopeActive()
                    && TryQueueHumanOutputBehindControlBarrier(QueuedHumanOutput.RetainedText(text, isError)))
                {
                    return;
                }
            }

            if (isError)
            {
                if (IsHumanInteractive())
                {
                    WriteHumanControlOutput(text, isError: true);
                }
                else
                {
                    Console.Error.WriteLine(text);
                }
            }
            else
            {
                if (IsHumanInteractive())
                {
                    WriteHumanControlOutput(text, isError: false);
                }
                else
                {
                    Console.WriteLine(text);
                }
            }

        }
    }

    public void WriteErrorLineOnce(string message, bool countAsFailure = true)
    {
        var normalized = ErrorNoticePresenter.NormalizeMessage(message);
        if (string.Equals(lastDisplayedErrorMessage, normalized, StringComparison.Ordinal))
        {
            if (countAsFailure)
            {
                context.MarkFailure();
                context.SetLastFailureMessage(normalized);
            }

            return;
        }

        lastDisplayedErrorMessage = normalized;
        WriteDisplayBlock(
            ErrorNoticePresenter.BuildBlock(normalized),
            isError: true,
            countAsFailure: countAsFailure);
    }

    public void CloseAssistantLineIfOpen()
    {
        if (!context.GetAssistantLineOpen())
        {
            return;
        }

        if (IsHumanInteractive())
        {
            lock (context.ConsoleGate)
            {
                terminalFrameWriter.ClearCurrentFrameForOutput();
            }
        }
        else
        {
            WriteLine(string.Empty);
        }

        context.SetAssistantLineOpen(false);
        terminalFrameWriter.RefreshFinalDock();
    }

    private bool IsHumanInteractive()
        => context.GetOutputProtocol() == ChatOutputProtocol.Human && !context.IsScriptMode();

    private void WriteHumanControlOutput(string text, bool isError)
    {
        if (TryBufferControlOutput(text, isError))
        {
            return;
        }

        var outputText = IsControlOutputScopeActive()
            ? ControlOutputBoxRenderer.Render(text, isError, ResolveHumanOutputWidth(), styled: true)
            : text;
        terminalFrameWriter.WriteRetainedText(outputText, isError: !IsControlOutputScopeActive() && isError);
    }

    private bool TryQueueHumanOutputBehindControlBarrier(
        QueuedHumanOutput output,
        bool includeCurrentScope = false)
    {
        var state = activeBufferedControlOutputScope;
        if (state is null || (!includeCurrentScope && CurrentControlOutputScope.Value is not null))
        {
            return false;
        }

        state.QueuedOutputs.Add(output);
        return true;
    }

    private static bool ShouldRenderDisplayBlockAsControlOutput(ChatPresentationBlock block, bool isError)
        => isError || block is SystemNoticeBlock;

    private static bool IsControlOutputScopeActive()
        => CurrentControlOutputScope.Value is not null;

    private static bool TryBufferControlOutput(string text, bool isError)
    {
        var state = CurrentControlOutputScope.Value;
        if (state is null || !state.Buffered)
        {
            return false;
        }

        state.Entries.Add(new ControlOutputLine(text, isError));
        return true;
    }

    private void EndControlOutputScope(ControlOutputState state)
    {
        var ownsCurrentScope = ReferenceEquals(CurrentControlOutputScope.Value, state);
        if (!ownsCurrentScope && (!state.Buffered || !ReferenceEquals(activeBufferedControlOutputScope, state)))
        {
            return;
        }

        if (ownsCurrentScope)
        {
            CurrentControlOutputScope.Value = state.Parent;
        }

        if (!state.Buffered)
        {
            return;
        }

        state.Closed = true;
        if (state.Parent is { Buffered: true } parent)
        {
            parent.Entries.AddRange(state.Entries);
            parent.QueuedOutputs.AddRange(state.QueuedOutputs);
            return;
        }

        if (state.QueueExternalOutput
            && activeBufferedControlOutputScope is { Closed: false } active
            && !ReferenceEquals(active, state))
        {
            lock (context.ConsoleGate)
            {
                active.Entries.AddRange(state.Entries);
                active.QueuedOutputs.AddRange(state.QueuedOutputs);
            }

            return;
        }

        if (ReferenceEquals(activeBufferedControlOutputScope, state))
        {
            activeBufferedControlOutputScope = null;
        }

        FlushControlOutputLines(state.Entries, state.QueuedOutputs);
    }

    private void FlushControlOutputLines(
        IReadOnlyList<ControlOutputLine> entries,
        IReadOnlyList<QueuedHumanOutput>? queuedOutputs = null)
    {
        lock (context.ConsoleGate)
        {
            terminalFrameWriter.ClearCurrentFrameForOutput();
            if (entries.Count > 0)
            {
                var outputText = ControlOutputBoxRenderer.Render(entries, ResolveHumanOutputWidth(), styled: true);
                terminalFrameWriter.WriteRetainedTextInCurrentFrame(outputText, isError: false);
            }

            if (queuedOutputs is not null)
            {
                var refreshAfterQueuedOutputs = false;
                foreach (var queuedOutput in queuedOutputs)
                {
                    refreshAfterQueuedOutputs |= queuedOutput.WriteTo(terminalFrameWriter);
                }

                if (refreshAfterQueuedOutputs)
                {
                    terminalFrameWriter.RefreshFinalDock();
                    return;
                }
            }

            terminalFrameWriter.RefreshFinalDock();
        }
    }

    private int ResolveHumanOutputWidth()
    {
        try
        {
            return context.TerminalHost.BuildTerminalFrameContextFromDock(
                    context.TerminalHost.BuildCurrentComposerDockState(),
                    includePopupLines: false,
                    styled: false)
                .Width;
        }
        catch (IOException)
        {
            return TerminalLayoutCalculator.SafeWritableWidth();
        }
        catch (ArgumentOutOfRangeException)
        {
            return TerminalLayoutCalculator.SafeWritableWidth();
        }
        catch (InvalidOperationException)
        {
            return TerminalLayoutCalculator.SafeWritableWidth();
        }
    }

    private sealed class ControlOutputScope(ChatOutputWriter owner, ControlOutputState state) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            owner.EndControlOutputScope(state);
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        private NoopDisposable()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ExclusiveTerminalFrameScope(
        object syncRoot,
        TerminalFrameWriteCoordinator terminalFrameWriter) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lock (syncRoot)
            {
                terminalFrameWriter.RefreshFinalDock();
            }
        }
    }

    private sealed class ControlOutputState(ControlOutputState? parent, bool buffered, bool queueExternalOutput)
    {
        public ControlOutputState? Parent { get; } = parent;

        public bool Buffered { get; } = buffered;

        public bool QueueExternalOutput { get; } = queueExternalOutput;

        public bool Closed { get; set; }

        public List<ControlOutputLine> Entries { get; } = [];

        public List<QueuedHumanOutput> QueuedOutputs { get; } = [];
    }

    private sealed class QueuedHumanOutput
    {
        private QueuedHumanOutput(
            QueuedHumanOutputKind kind,
            string? text = null,
            ChatPresentationBlock? block = null,
            bool isError = false,
            bool refreshAfter = false)
        {
            Kind = kind;
            Text = text;
            Block = block;
            IsError = isError;
            RefreshAfter = refreshAfter;
        }

        private QueuedHumanOutputKind Kind { get; }

        private string? Text { get; }

        private ChatPresentationBlock? Block { get; }

        private bool IsError { get; }

        private bool RefreshAfter { get; }

        public static QueuedHumanOutput AssistantRetainedFrame()
            => new(QueuedHumanOutputKind.AssistantRetainedFrame);

        public static QueuedHumanOutput BlankLine()
            => new(QueuedHumanOutputKind.BlankLine);

        public static QueuedHumanOutput PresentationBlock(
            ChatPresentationBlock block,
            bool isError,
            bool refreshAfter = false)
            => new(QueuedHumanOutputKind.PresentationBlock, block: block, isError: isError, refreshAfter: refreshAfter);

        public static QueuedHumanOutput RetainedText(string text, bool isError)
            => new(QueuedHumanOutputKind.RetainedText, text: text, isError: isError);

        public bool WriteTo(TerminalFrameWriteCoordinator terminalFrameWriter)
        {
            ArgumentNullException.ThrowIfNull(terminalFrameWriter);

            switch (Kind)
            {
                case QueuedHumanOutputKind.AssistantRetainedFrame:
                    terminalFrameWriter.RenderAssistantRetainedTailFrameInCurrentFrame();
                    break;
                case QueuedHumanOutputKind.BlankLine:
                    TerminalFrameWriteCoordinator.WriteBlankLineInCurrentFrame();
                    break;
                case QueuedHumanOutputKind.PresentationBlock when Block is not null:
                    terminalFrameWriter.WritePresentationBlockInCurrentFrame(Block, IsError);
                    break;
                case QueuedHumanOutputKind.RetainedText when Text is not null:
                    terminalFrameWriter.WriteRetainedTextInCurrentFrame(Text, IsError);
                    break;
            }

            return RefreshAfter;
        }
    }

    private enum QueuedHumanOutputKind
    {
        AssistantRetainedFrame,
        BlankLine,
        PresentationBlock,
        RetainedText,
    }
}
