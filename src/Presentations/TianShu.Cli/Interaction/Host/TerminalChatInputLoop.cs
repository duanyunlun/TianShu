using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Owns the native terminal composer input loop for interactive chat.
/// 负责交互式 chat 的原生终端输入循环。
/// </summary>
internal sealed class TerminalChatInputLoop
{
    private readonly Func<CancellationToken, Task<TerminalInputKey?>> readKeyAsync;

    public TerminalChatInputLoop(Func<CancellationToken, Task<TerminalInputKey?>>? readKeyAsync = null)
        => this.readKeyAsync = readKeyAsync ?? ConsoleTerminalInput.Shared.ReadKeyAsync;

    public async Task<TerminalChatInputLoopResult> RunAsync(
        TerminalChatInputLoopContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var historyScopeKey = context.GetInputHistoryScopeKey();
        var composer = new TerminalChatComposer(LoadInputHistory(context, historyScopeKey));
        var renderer = new TerminalPromptRenderer();
        var suggestions = new TerminalSuggestionPopup(context.Options.WorkingDirectory);
        var placeholder = TerminalStartupBanner.BuildPlaceholder(context.Options, styled: true);
        var selectedSuggestionIndex = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                RefreshComposerHistoryIfScopeChanged(context, composer, ref historyScopeKey);
                var suggestionFrame = suggestions.Build(composer.Text, composer.Cursor, selectedSuggestionIndex);
                selectedSuggestionIndex = Math.Max(suggestionFrame.SelectedIndex, 0);
                RenderPrompt(context, composer, renderer, suggestionFrame.RenderLines, placeholder);

                var key = await readKeyAsync(cancellationToken).ConfigureAwait(false);
                if (key is null)
                {
                    continue;
                }

                if (HandleQueuedFollowUpDockNavigation(context, key.Value, ref selectedSuggestionIndex))
                {
                    continue;
                }

                if (await HandleQueuedFollowUpDockPromotionAsync(context, key.Value, composer, cancellationToken).ConfigureAwait(false))
                {
                    selectedSuggestionIndex = 0;
                    continue;
                }

                if (HandleSuggestionKey(suggestionFrame, key.Value, composer, ref selectedSuggestionIndex))
                {
                    continue;
                }

                var action = composer.HandleKey(key.Value);
                if (action.Kind == TerminalComposerActionKind.Exit)
                {
                    context.CompleteInputLine(renderer, false, null);
                    return TerminalChatInputLoopResult.ExitRequested;
                }

                if (action.Kind != TerminalComposerActionKind.Submit || string.IsNullOrWhiteSpace(action.Text))
                {
                    if (key.Value.Kind is not (TerminalKeyKind.UpArrow or TerminalKeyKind.DownArrow))
                    {
                        selectedSuggestionIndex = 0;
                    }

                    continue;
                }

                context.CompleteInputLine(renderer, true, action.Text);

                var shouldExit = await context.SubmitLineAsync(action.Text!, action.SubmitIntent, cancellationToken).ConfigureAwait(false);
                context.RecordSubmittedInput(action.Text!, action.SubmitIntent);
                RefreshComposerHistoryIfScopeChanged(context, composer, ref historyScopeKey, forceWhenEmpty: true);
                if (shouldExit)
                {
                    return TerminalChatInputLoopResult.ExitRequested;
                }

                RenderPrompt(context, composer, renderer, popupLines: null, placeholder);
            }
        }
        finally
        {
            context.ResetTerminal();
        }

        return TerminalChatInputLoopResult.Continue;
    }

    public async Task<string?> ReadConfirmationAsync(
        TerminalChatInputLoopContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var composer = new TerminalChatComposer();
        var renderer = new TerminalPromptRenderer();
        const string placeholder = "请输入确认文本";
        while (!cancellationToken.IsCancellationRequested)
        {
            RenderPrompt(context, composer, renderer, popupLines: null, placeholder);
            var key = await readKeyAsync(cancellationToken).ConfigureAwait(false);
            if (key is null)
            {
                continue;
            }

            if (key.Value.Kind == TerminalKeyKind.Enter)
            {
                var text = composer.Text.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                context.CompleteInputLine(renderer, true, text);
                return text;
            }

            var action = composer.HandleKey(key.Value);
            if (action.Kind == TerminalComposerActionKind.Exit)
            {
                context.CompleteInputLine(renderer, false, null);
                return null;
            }
        }

        return null;
    }

    private static bool HandleSuggestionKey(
        TerminalSuggestionPopupFrame suggestionFrame,
        TerminalInputKey key,
        TerminalChatComposer composer,
        ref int selectedSuggestionIndex)
    {
        if (!suggestionFrame.HasItems)
        {
            return false;
        }

        if (key.Kind == TerminalKeyKind.UpArrow)
        {
            selectedSuggestionIndex = suggestionFrame.MoveSelection(-1).SelectedIndex;
            return true;
        }

        if (key.Kind == TerminalKeyKind.DownArrow)
        {
            selectedSuggestionIndex = suggestionFrame.MoveSelection(1).SelectedIndex;
            return true;
        }

        if (key.Kind == TerminalKeyKind.Tab && suggestionFrame.SelectedItem is { } selectedSuggestion)
        {
            composer.ReplaceRange(
                selectedSuggestion.ReplaceStart,
                selectedSuggestion.ReplaceLength,
                selectedSuggestion.InsertText);
            selectedSuggestionIndex = 0;
            return true;
        }

        return false;
    }

    private static bool HandleQueuedFollowUpDockNavigation(
        TerminalChatInputLoopContext context,
        TerminalInputKey key,
        ref int selectedSuggestionIndex)
    {
        if ((key.Modifiers & TerminalKeyModifiers.Control) == 0)
        {
            return false;
        }

        var delta = key.Kind switch
        {
            TerminalKeyKind.UpArrow => -1,
            TerminalKeyKind.DownArrow => 1,
            _ => 0,
        };
        if (delta == 0)
        {
            return false;
        }

        selectedSuggestionIndex = 0;
        _ = context.MoveQueuedFollowUpSelection(delta);
        return true;
    }

    private static async Task<bool> HandleQueuedFollowUpDockPromotionAsync(
        TerminalChatInputLoopContext context,
        TerminalInputKey key,
        TerminalChatComposer composer,
        CancellationToken cancellationToken)
    {
        if (key.Kind != TerminalKeyKind.Enter
            || key.Modifiers != TerminalKeyModifiers.None
            || !composer.IsEmpty)
        {
            return false;
        }

        return await context.PromoteSelectedQueuedFollowUpAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void RenderPrompt(
        TerminalChatInputLoopContext context,
        TerminalChatComposer composer,
        TerminalPromptRenderer renderer,
        IReadOnlyList<string>? popupLines,
        string? placeholder)
        => context.RenderPrompt(
            composer,
            renderer,
            context.BuildPrompt(),
            popupLines,
            placeholder);

    private static void RefreshComposerHistoryIfScopeChanged(
        TerminalChatInputLoopContext context,
        TerminalChatComposer composer,
        ref string? historyScopeKey,
        bool forceWhenEmpty = false)
    {
        if (!composer.IsEmpty)
        {
            return;
        }

        var nextScopeKey = context.GetInputHistoryScopeKey();
        if (forceWhenEmpty && string.IsNullOrWhiteSpace(nextScopeKey))
        {
            return;
        }

        if (!forceWhenEmpty && string.Equals(historyScopeKey, nextScopeKey, StringComparison.Ordinal))
        {
            return;
        }

        historyScopeKey = nextScopeKey;
        composer.ReplaceHistory(LoadInputHistory(context, nextScopeKey));
    }

    private static IReadOnlyList<string> LoadInputHistory(TerminalChatInputLoopContext context, string? scopeKey)
    {
        var history = context.LoadInputHistory(scopeKey);
        return history.Count > 0 ? history : context.InitialInputHistory;
    }
}
