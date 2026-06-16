using TianShu.Cli.Interaction.Rendering;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Owns the terminal-frame lifecycle for the interactive chat surface.
/// 持有交互式 chat 终端帧生命周期状态，避免 runner 直接维护 Dock、prompt 与 retained tail 细节。
/// </summary>
internal sealed class TerminalInteractionHost : IChatOutputTerminalHost, IDisposable
{
    private readonly object syncRoot;
    private readonly Func<bool> isHumanOutput;
    private readonly Func<bool> isScriptMode;
    private readonly Func<bool> isBusy;
    private readonly Func<ModelDockSummary?> getModelSummary;
    private readonly Func<PlanDockSummary?> getPlan;
    private readonly Func<QueuedFollowUpDockState?> getQueuedFollowUps;
    private readonly Func<string?> getInputNotice;
    private readonly Func<TimeSpan?> getWorkingElapsed;
    private readonly Func<InteractionPipeline> getPipeline;
    private readonly Func<bool> shouldSkipWorkingDockRefresh;
    private readonly Func<IDisposable> hideCursorForRefresh;
    private readonly TerminalChatFrameRenderer terminalChatFrameRenderer = new();
    private readonly TerminalPromptFrameController promptFrame;
    private readonly AssistantRetainedTailController assistantTail;
    private readonly WaitingPlaceholderController waitingPlaceholder;
    private readonly WorkingDockTimerController workingDockRefreshTimer;
    private ComposerDockState? currentComposerDockState;
    private IReadOnlyList<string> currentComposerDockPopupLines = Array.Empty<string>();
    private CommandOverlayDockState? commandOverlay;
    private CommandOverlayScope? activeCommandOverlayScope;

    public TerminalInteractionHost(
        object syncRoot,
        Func<bool> isHumanOutput,
        Func<bool> isScriptMode,
        Func<bool> isBusy,
        Func<ModelDockSummary?> getModelSummary,
        Func<PlanDockSummary?> getPlan,
        Func<QueuedFollowUpDockState?> getQueuedFollowUps,
        Func<string?> getInputNotice,
        Func<TimeSpan?> getWorkingElapsed,
        Func<InteractionPipeline> getPipeline,
        Func<bool> shouldSkipWorkingDockRefresh,
        Func<IDisposable> hideCursorForRefresh)
    {
        this.syncRoot = syncRoot ?? throw new ArgumentNullException(nameof(syncRoot));
        this.isHumanOutput = isHumanOutput ?? throw new ArgumentNullException(nameof(isHumanOutput));
        this.isScriptMode = isScriptMode ?? throw new ArgumentNullException(nameof(isScriptMode));
        this.isBusy = isBusy ?? throw new ArgumentNullException(nameof(isBusy));
        this.getModelSummary = getModelSummary ?? throw new ArgumentNullException(nameof(getModelSummary));
        this.getPlan = getPlan ?? throw new ArgumentNullException(nameof(getPlan));
        this.getQueuedFollowUps = getQueuedFollowUps ?? throw new ArgumentNullException(nameof(getQueuedFollowUps));
        this.getInputNotice = getInputNotice ?? throw new ArgumentNullException(nameof(getInputNotice));
        this.getWorkingElapsed = getWorkingElapsed ?? throw new ArgumentNullException(nameof(getWorkingElapsed));
        this.getPipeline = getPipeline ?? throw new ArgumentNullException(nameof(getPipeline));
        this.shouldSkipWorkingDockRefresh = shouldSkipWorkingDockRefresh ?? throw new ArgumentNullException(nameof(shouldSkipWorkingDockRefresh));
        this.hideCursorForRefresh = hideCursorForRefresh ?? throw new ArgumentNullException(nameof(hideCursorForRefresh));
        promptFrame = new TerminalPromptFrameController(this.hideCursorForRefresh);
        assistantTail = new AssistantRetainedTailController(
            this.isHumanOutput,
            HasUncommittedAssistantRetainedText,
            ReadUncommittedAssistantRetainedText,
            this.getPipeline,
            this.getPlan,
            BuildCurrentComposerDockState,
            BuildTerminalFrameContextFromDock,
            dockState => currentComposerDockState = dockState,
            this.hideCursorForRefresh,
            promptFrame);
        waitingPlaceholder = new WaitingPlaceholderController(this.syncRoot, this.isHumanOutput);
        workingDockRefreshTimer = new WorkingDockTimerController(
            this.syncRoot,
            this.isHumanOutput,
            this.isScriptMode,
            RefreshWorkingDockTick);
    }

    public static bool ShouldShowWaitingPlaceholder(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0
            && !trimmed.StartsWith('!')
            && !trimmed.StartsWith('/');
    }

    public bool HasRetainedTailFrame => assistantTail.HasFrame;

    public void RenderPrompt(
        TerminalChatComposer composer,
        TerminalPromptRenderer renderer,
        string prompt,
        IReadOnlyList<string>? popupLines = null,
        string? placeholder = null)
    {
        if (!isHumanOutput())
        {
            return;
        }

        var dockState = BuildComposerDockState(composer, prompt);
        lock (syncRoot)
        {
            using var cursorScope = hideCursorForRefresh();
            currentComposerDockState = dockState;
            currentComposerDockPopupLines = popupLines ?? Array.Empty<string>();
            var frame = new TerminalRenderFrame(
                prompt,
                composer.Text,
                composer.Cursor,
                dockState.CommandOverlay is { Lines.Count: > 0 } ? null : popupLines,
                placeholder,
                AboveInputLines: ComposerDockRenderer.BuildPromptAboveInputLines(
                    dockState,
                    styled: true,
                    TerminalLayoutCalculator.SafeWritableWidth()),
                FooterLines: ComposerDockRenderer.BuildPromptFooterLines(
                    dockState,
                    styled: true,
                    TerminalLayoutCalculator.SafeWritableWidth()),
                LeadingBlankLineCount: ResolvePromptLeadingBlankLineCount(),
                OverrideLines: BuildPromptOverrideLines(dockState));
            promptFrame.SetFrame(renderer, frame);
            if (assistantTail.HasUncommittedText())
            {
                assistantTail.RenderUnsafe();
                return;
            }

            if (assistantTail.HasFrame)
            {
                assistantTail.ClearUnsafe();
            }

            promptFrame.RenderCurrent();
        }
    }

    public void CompleteInputLine(
        TerminalPromptRenderer? renderer = null,
        bool addVisualSpacerAfter = false,
        bool clearSubmittedInput = false)
    {
        if (!isHumanOutput())
        {
            return;
        }

        lock (syncRoot)
        {
            if (clearSubmittedInput && assistantTail.HasFrame)
            {
                assistantTail.CommitUnsafe();
                return;
            }

            promptFrame.CompleteInputLine(renderer, addVisualSpacerAfter, clearSubmittedInput);
        }
    }

    public void StartWaitingPlaceholder()
        => waitingPlaceholder.Start();

    public void StopWaitingPlaceholder(bool clearLine)
        => waitingPlaceholder.Stop(clearLine);

    public void StopWaitingPlaceholderUnsafe(bool clearLine)
        => waitingPlaceholder.StopUnsafe(clearLine);

    public void StartWorkingDockTimer()
        => workingDockRefreshTimer.Start();

    public void StopWorkingDockTimer()
        => workingDockRefreshTimer.Stop();

    public void RefreshWorkingDockTick()
    {
        if (!isHumanOutput() || isScriptMode())
        {
            return;
        }

        if (!Monitor.TryEnter(syncRoot))
        {
            return;
        }

        try
        {
            if (assistantTail.HasUncommittedText())
            {
                assistantTail.RenderUnsafe();
                return;
            }

            if (shouldSkipWorkingDockRefresh())
            {
                return;
            }

            RefreshAndRestoreInlineTailPromptUnsafe();
        }
        finally
        {
            Monitor.Exit(syncRoot);
        }
    }

    public bool PrepareInlineTailPromptWrite(bool assistantLineOpen)
    {
        StopWaitingPlaceholderUnsafe(clearLine: true);
        if (isScriptMode())
        {
            return false;
        }

        if (assistantTail.HasFrame)
        {
            if (assistantLineOpen)
            {
                assistantTail.CommitUnsafe();
            }
            else
            {
                assistantTail.ClearUnsafe();
            }

            return true;
        }

        return promptFrame.ClearForInlineTailWrite();
    }

    public void RestoreInlineTailPrompt()
        => promptFrame.RestoreInlineTailPrompt();

    public void RenderAssistantRetainedTailFrameUnsafe()
        => assistantTail.RenderUnsafe();

    public void CommitAssistantRetainedTailFrameUnsafe()
        => assistantTail.CommitUnsafe();

    public void ClearAssistantRetainedTailFrameUnsafe()
        => assistantTail.ClearUnsafe();

    public TerminalFrameBuildContext BuildTerminalFrameContextFromDock(
        ComposerDockState dockState,
        bool includePopupLines,
        bool styled)
        => new(
            dockState.InputText,
            dockState.Cursor,
            dockState.Prompt,
            dockState.Agents,
            dockState.Model,
            dockState.IsBusy,
            dockState.WorkingElapsed,
            dockState.QueuedFollowUps,
            dockState.InputNotice,
            dockState.CommandOverlay,
            includePopupLines ? currentComposerDockPopupLines : null,
            TerminalLayoutCalculator.SafeWritableWidth(),
            Styled: styled);

    public ComposerDockState BuildCurrentComposerDockState()
    {
        var frame = promptFrame.CurrentFrame;
        return new ComposerDockState(
            currentComposerDockState?.InputText ?? frame?.Text ?? string.Empty,
            currentComposerDockState?.Cursor ?? frame?.Cursor ?? 0,
            currentComposerDockState?.Prompt ?? frame?.Prompt ?? "> ",
            getPlan(),
            currentComposerDockState?.Agents ?? new AgentDockSummary(0),
            currentComposerDockState?.Model ?? getModelSummary(),
            isBusy(),
            getWorkingElapsed(),
            getQueuedFollowUps(),
            getInputNotice(),
            commandOverlay);
    }

    public void RefreshAndRestoreInlineTailPrompt()
    {
        if (!isHumanOutput())
        {
            return;
        }

        lock (syncRoot)
        {
            RefreshAndRestoreInlineTailPromptUnsafe();
        }
    }

    public IDisposable BeginCommandOverlay(Action? onEscape = null)
    {
        if (!isHumanOutput() || isScriptMode())
        {
            return NoopDisposable.Instance;
        }

        var scope = new CommandOverlayScope(this, onEscape);
        lock (syncRoot)
        {
            StopWaitingPlaceholderUnsafe(clearLine: true);
            activeCommandOverlayScope = scope;
            commandOverlay = new CommandOverlayDockState(Array.Empty<string>());
            RefreshAndRestoreInlineTailPromptUnsafe();
        }

        scope.StartEscapeMonitor();
        return scope;
    }

    public void SetCommandOverlayLines(IReadOnlyList<string> lines)
    {
        if (!isHumanOutput() || isScriptMode())
        {
            return;
        }

        lock (syncRoot)
        {
            if (activeCommandOverlayScope is null)
            {
                return;
            }

            commandOverlay = new CommandOverlayDockState(lines.ToArray());
            RefreshAndRestoreInlineTailPromptUnsafe();
        }
    }

    public void WriteHumanTerminalRetainedText(string text, bool isError)
    {
        if (string.IsNullOrEmpty(text))
        {
            Console.WriteLine();
            return;
        }

        var displayText = isError ? TerminalAnsi.RedText(text) : text;
        var width = TerminalLayoutCalculator.SafeWritableWidth();
        var dockState = currentComposerDockState
            ?? new ComposerDockState(
                InputText: promptFrame.CurrentFrame?.Text ?? string.Empty,
                Cursor: promptFrame.CurrentFrame?.Cursor ?? 0,
                Prompt: promptFrame.CurrentFrame?.Prompt ?? "> ",
                Plan: getPlan(),
                Agents: new AgentDockSummary(0),
                Model: null,
                IsBusy: false,
                WorkingElapsed: getWorkingElapsed(),
                QueuedFollowUps: getQueuedFollowUps(),
                InputNotice: getInputNotice(),
                CommandOverlay: commandOverlay);
        var frame = new TerminalChatFrame(
            [displayText],
            dockState,
            PopupLines: null,
            Width: width);

        foreach (var line in terminalChatFrameRenderer.RenderTranscriptLines(frame))
        {
            if (isError)
            {
                Console.Error.WriteLine(line);
            }
            else
            {
                Console.WriteLine(line);
            }
        }
    }

    public void WriteHumanTerminalPresentationBlock(ChatPresentationBlock block, bool isError)
    {
        var frame = getPipeline().BuildPresentationBlockFrame(
            block,
            getPlan(),
            BuildTerminalFrameContextFromDock(BuildCurrentComposerDockState(), includePopupLines: false, styled: true));
        foreach (var line in terminalChatFrameRenderer.RenderTranscriptLines(frame))
        {
            if (isError)
            {
                Console.Error.WriteLine(line);
            }
            else
            {
                Console.WriteLine(line);
            }
        }
    }

    public void Reset()
    {
        StopWaitingPlaceholderUnsafe(clearLine: false);
        StopWorkingDockTimer();
        currentComposerDockState = null;
        currentComposerDockPopupLines = Array.Empty<string>();
        commandOverlay = null;
        activeCommandOverlayScope = null;
        promptFrame.Reset();
        assistantTail.Reset();
    }

    public void Dispose()
    {
        waitingPlaceholder.Dispose();
        workingDockRefreshTimer.Dispose();
    }

    private ComposerDockState BuildComposerDockState(
        TerminalChatComposer composer,
        string prompt)
        => new(
            composer.Text,
            composer.Cursor,
            prompt,
            getPlan(),
            new AgentDockSummary(0),
            getModelSummary(),
            isBusy(),
            getWorkingElapsed(),
            getQueuedFollowUps(),
            getInputNotice(),
            commandOverlay);

    private int ResolvePromptLeadingBlankLineCount()
        => Math.Max(
            promptFrame.CurrentFrame?.LeadingBlankLineCount ?? 0,
            TerminalChatFrameRenderer.DockLeadingBlankLineCount);

    private static IReadOnlyList<string>? BuildPromptOverrideLines(ComposerDockState state)
        => state.CommandOverlay is { Lines.Count: > 0 }
            ? new ComposerDockRenderer().BuildDockLines(
                state,
                popupLines: null,
                styled: true,
                TerminalLayoutCalculator.SafeWritableWidth())
            : null;

    private void RefreshAndRestoreInlineTailPromptUnsafe()
    {
        if (assistantTail.HasUncommittedText())
        {
            assistantTail.RenderUnsafe();
            return;
        }

        if (assistantTail.HasFrame)
        {
            assistantTail.ClearUnsafe();
        }

        var frame = promptFrame.CurrentFrame;
        if (frame is null)
        {
            return;
        }

        var state = new ComposerDockState(
            frame.Value.Text,
            frame.Value.Cursor,
            frame.Value.Prompt,
            getPlan(),
            new AgentDockSummary(0),
            getModelSummary(),
            isBusy(),
            getWorkingElapsed(),
            getQueuedFollowUps(),
            getInputNotice(),
            commandOverlay);
        currentComposerDockState = state;
        if (promptFrame.UpdateFooterLines(state))
        {
            promptFrame.RestoreInlineTailPrompt();
        }
    }

    private void EndCommandOverlay(CommandOverlayScope scope)
    {
        lock (syncRoot)
        {
            if (!ReferenceEquals(activeCommandOverlayScope, scope))
            {
                return;
            }

            activeCommandOverlayScope = null;
            commandOverlay = null;
            RefreshAndRestoreInlineTailPromptUnsafe();
        }
    }

    private bool HasUncommittedAssistantRetainedText()
        => getPipeline().PresentationState.HasActiveAssistantText;

    private string ReadUncommittedAssistantRetainedText()
        => getPipeline().PresentationState.ActiveAssistantText;

    private sealed class CommandOverlayScope(TerminalInteractionHost owner, Action? onEscape) : IDisposable
    {
        private TerminalInteractionHost? owner = owner;
        private readonly CancellationTokenSource escapeMonitorCancellation = new();
        private readonly Action? onEscape = onEscape;
        private Task? escapeMonitorTask;

        public void StartEscapeMonitor()
            => escapeMonitorTask = Task.Run(MonitorEscapeAsync);

        public void Dispose()
        {
            var currentOwner = Interlocked.Exchange(ref owner, null);
            escapeMonitorCancellation.Cancel();
            _ = (escapeMonitorTask ?? Task.CompletedTask).ContinueWith(
                _ => escapeMonitorCancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            currentOwner?.EndCommandOverlay(this);
        }

        private async Task MonitorEscapeAsync()
        {
            if (Console.IsInputRedirected)
            {
                return;
            }

            var token = escapeMonitorCancellation.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Escape)
                        {
                            onEscape?.Invoke();
                            return;
                        }
                    }

                    await Task.Delay(50, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
            }
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
}
